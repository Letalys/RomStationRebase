using System.Diagnostics;
using java.sql;
using org.apache.derby.jdbc;
using RomStationRebase.Models;
using SystemFile      = System.IO.File;
using SystemPath      = System.IO.Path;
using SystemDirectory = System.IO.Directory;

namespace RomStationRebase.Services;

/// <summary>
/// Accès à la base Derby de RomStation via IKVM.
/// Toutes les opérations travaillent sur une COPIE de la base, jamais l'originale.
/// </summary>
public class DerbyService
{
    private const string LockFileName = "db.lck";

    // ── Vérification du verrou ────────────────────────────────────────────

    /// <summary>
    /// Indique si RomStation est ouvert et verrouille la base.
    /// Derby crée db.lck dans le dossier de la base pendant qu'elle est ouverte.
    /// </summary>
    /// <param name="dbPath">Chemin du dossier de la base (originale ou copie).</param>
    public bool IsDatabaseLocked(string dbPath)
        => SystemFile.Exists(SystemPath.Combine(dbPath, LockFileName));

    // ── Copie de la base ──────────────────────────────────────────────────

    /// <summary>
    /// Copie récursivement le dossier database de RomStation vers destinationPath.
    /// Retourne false si la base source est verrouillée ou si une erreur survient.
    /// </summary>
    /// <param name="sourcePath">Dossier database de l'installation RomStation.</param>
    /// <param name="destinationPath">Dossier de destination pour la copie RSRebase.</param>
    public bool CopyDatabase(string sourcePath, string destinationPath)
    {
        if (IsDatabaseLocked(sourcePath))
        {
            Console.WriteLine("[DerbyService] Copie annulée : la base est verrouillée (RomStation est ouvert).");
            return false;
        }

        try
        {
            // Recrée le dossier destination proprement à chaque sync
            if (SystemDirectory.Exists(destinationPath))
                SystemDirectory.Delete(destinationPath, recursive: true);

            CopyDirectoryRecursive(sourcePath, destinationPath);
            Console.WriteLine($"[DerbyService] Base copiée vers : {destinationPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerbyService] Erreur lors de la copie : {ex.Message}");
            return false;
        }
    }

    /// <summary>Copie un dossier et tout son contenu de manière récursive.</summary>
    private static void CopyDirectoryRecursive(string source, string destination)
    {
        SystemDirectory.CreateDirectory(destination);

        foreach (string file in SystemDirectory.GetFiles(source))
            SystemFile.Copy(file, SystemPath.Combine(destination, SystemPath.GetFileName(file)), overwrite: true);

        foreach (string subDir in SystemDirectory.GetDirectories(source))
            CopyDirectoryRecursive(subDir, SystemPath.Combine(destination, SystemPath.GetFileName(subDir)));
    }

    // ── Lecture des données ───────────────────────────────────────────────

    /// <summary>
    /// Retourne le nombre total de jeux distincts dans la base Derby.
    /// Utilisé pour initialiser la barre de progression du chargement lazy dans MainWindow.
    /// </summary>
    /// <param name="dbCopyPath">Chemin du dossier de la copie de la base Derby.</param>
    public int GetGameCount(string dbCopyPath)
    {
        const string sql = "SELECT COUNT(DISTINCT g.ID) FROM APP.GAME g";
        using var conn = OpenConnection(dbCopyPath);
        if (conn is null) return 0;
        var stmt  = conn.createStatement();
        var rs    = stmt.executeQuery(sql);
        int count = rs.next() ? rs.getInt(1) : 0;
        rs.close();
        stmt.close();
        return count;
    }

    /// <summary>
    /// Ouvre une connexion JDBC vers la copie de la base Derby et retourne tous les jeux.
    /// Jointure avec GAME_FILE (LEFT JOIN) pour le nombre de fichiers et le répertoire racine.
    /// Jointure avec IMAGE (LEFT JOIN) pour l'image du système associé.
    /// </summary>
    /// <param name="dbCopyPath">Chemin du dossier de la copie de la base Derby.</param>
    /// <param name="romStationPath">Chemin de l'installation RomStation — utilisé pour construire les chemins de jaquettes.</param>
    public List<GameRecord> GetGames(string dbCopyPath, string romStationPath)
    {
        var games = new List<GameRecord>();

        // GROUP BY pour agréger les fichiers multiples (multi-disques) par jeu.
        // MIN(gf.DIRECTORY) donne un des répertoires du jeu, à partir duquel on reconstitue le chemin de la jaquette.
        // L'alias SYSTEM_IMAGE_PATH est joint depuis APP.IMAGE via la clé étrangère GRAPHIC_IMAGE_ID du système.
        const string sql = """
            SELECT g.ID,
                   g.TITLE        AS GAME_NAME,
                   s.NAME         AS SYSTEM_NAME,
                   i.PATH         AS SYSTEM_IMAGE_PATH,
                   COUNT(gf.ID)   AS FILE_COUNT,
                   MIN(gf.DIRECTORY) AS GAME_DIRECTORY
            FROM APP.GAME g
            JOIN  APP.SYSTEM s    ON s.ID = g.SYSTEM_ID
            LEFT JOIN APP.IMAGE i ON i.ID = s.GRAPHIC_IMAGE_ID
            LEFT JOIN APP.GAME_FILE gf ON gf.GAME_ID = g.ID
            GROUP BY g.ID, g.TITLE, s.NAME, i.PATH
            ORDER BY SYSTEM_NAME, GAME_NAME
            """;

        using var conn = OpenConnection(dbCopyPath);
        if (conn is null) return games;

        var stmt = conn.createStatement();
        var rs   = stmt.executeQuery(sql);

        while (rs.next())
        {
            string? gameDirectory    = rs.getString("GAME_DIRECTORY");
            string? sysImageRaw      = rs.getString("SYSTEM_IMAGE_PATH");
            string  gameName         = rs.getString("GAME_NAME")   ?? string.Empty;
            string  sysName          = rs.getString("SYSTEM_NAME") ?? string.Empty;
            string  sysImagePath     = BuildSystemImagePath(sysImageRaw, romStationPath, sysName);
            string? coverPath        = BuildCoverPath(gameDirectory, romStationPath);

            games.Add(new GameRecord
            {
                Id              = rs.getInt("ID"),
                Title           = gameName,
                SystemName      = sysName,
                SystemImagePath = sysImagePath,
                GameDirectory   = gameDirectory                      ?? string.Empty,
                FileCount       = rs.getInt("FILE_COUNT"),
                CoverPath       = coverPath,
                CoverExists     = coverPath != null && SystemFile.Exists(coverPath),
                FileExists      = rs.getInt("FILE_COUNT") > 0,
            });
        }

        rs.close();
        stmt.close();
        return games;
    }

    /// <summary>
    /// Retourne la liste de tous les systèmes présents dans la base Derby, avec leur image.
    /// </summary>
    /// <param name="dbCopyPath">Chemin du dossier de la copie de la base Derby.</param>
    /// <param name="romStationPath">Chemin de l'installation RomStation — utilisé pour construire les chemins d'images.</param>
    public List<SystemRecord> GetSystems(string dbCopyPath, string romStationPath)
    {
        var systems = new List<SystemRecord>();

        const string sql = """
            SELECT s.ID, s.NAME, i.PATH AS IMAGE_PATH
            FROM APP.SYSTEM s
            LEFT JOIN APP.IMAGE i ON i.ID = s.GRAPHIC_IMAGE_ID
            ORDER BY s.NAME
            """;

        using var conn = OpenConnection(dbCopyPath);
        if (conn is null) return systems;

        var stmt = conn.createStatement();
        var rs   = stmt.executeQuery(sql);

        while (rs.next())
        {
            string  name         = rs.getString("NAME")       ?? string.Empty;
            string? imageRaw     = rs.getString("IMAGE_PATH");
            string  imagePath    = BuildSystemImagePath(imageRaw, romStationPath, name);

            systems.Add(new SystemRecord
            {
                Id        = rs.getInt("ID"),
                Name      = name,
                ImagePath = imagePath,
            });
        }

        rs.close();
        stmt.close();
        return systems;
    }

    // ── Fiche détail jeu ─────────────────────────────────────────────────

    /// <summary>
    /// Retourne la fiche complète d'un jeu depuis la base Derby.
    /// Locale supportée : "en" ou "fr". Tout autre tag est normalisé vers "en".
    /// La description et les genres utilisent un fallback vers "en" si la locale demandée est absente.
    /// Retourne null si le jeu est introuvable ou si la connexion échoue.
    /// </summary>
    /// <param name="dbCopyPath">Chemin du dossier de la copie de la base Derby.</param>
    /// <param name="romStationPath">Chemin de l'installation RomStation.</param>
    /// <param name="gameId">Identifiant Derby du jeu.</param>
    /// <param name="localeTag">Tag de locale ("en" ou "fr").</param>
    public GameDetail? GetGameDetail(string dbCopyPath, string romStationPath, int gameId, string localeTag)
    {
        // Normalisation de la locale : seuls "en" et "fr" sont supportés
        string locale = localeTag == "fr" ? "fr" : "en";

        using var conn = OpenConnection(dbCopyPath);
        if (conn is null) return null;

        try
        {
            // ── Requête 1 : données principales ──────────────────────────
            string? title         = null;
            int?    year          = null;
            int?    players       = null;
            string? developerName = null;
            string? publisherName = null;
            string? systemName    = null;
            string? systemImagePath = null;
            string? coverPath     = null;
            bool    coverExists   = false;
            string? gameDirectory = null;
            int?    descI18nId    = null;

            const string sqlMain = """
                SELECT g.TITLE,
                       g."YEAR",
                       g.PLAYERS,
                       g.DIRECTORY,
                       g.DESCRIPTION_I18N_ID,
                       d.NAME  AS DEVELOPER_NAME,
                       p.NAME  AS PUBLISHER_NAME,
                       s.NAME  AS SYSTEM_NAME,
                       si.PATH AS SYSTEM_IMAGE_PATH,
                       gi.PATH AS COVER_IMAGE_PATH
                FROM APP.GAME g
                LEFT JOIN APP.DEVELOPER d  ON d.ID = g.DEVELOPER_ID
                LEFT JOIN APP.PUBLISHER p  ON p.ID = g.PUBLISHER_ID
                JOIN  APP.SYSTEM s         ON s.ID = g.SYSTEM_ID
                LEFT JOIN APP.IMAGE si     ON si.ID = s.GRAPHIC_IMAGE_ID
                LEFT JOIN APP.IMAGE gi     ON gi.ID = g.GRAPHIC_IMAGE_ID
                WHERE g.ID = ?
                """;

            var ps1 = conn.prepareStatement(sqlMain);
            ps1.setInt(1, gameId);
            var rs1 = ps1.executeQuery();

            if (!rs1.next())
            {
                rs1.close(); ps1.close();
                Debug.WriteLine($"[DerbyService] GetGameDetail : jeu ID={gameId} introuvable.");
                return null;
            }

            title         = rs1.getString("TITLE")          ?? string.Empty;
            gameDirectory = rs1.getString("DIRECTORY")      ?? string.Empty;
            developerName = rs1.getString("DEVELOPER_NAME");
            publisherName = rs1.getString("PUBLISHER_NAME");
            systemName    = rs1.getString("SYSTEM_NAME")    ?? string.Empty;

            int rawYear    = rs1.getInt("YEAR");
            year           = rs1.wasNull() ? (int?)null : rawYear;
            int rawPlayers = rs1.getInt("PLAYERS");
            players        = rs1.wasNull() ? (int?)null : rawPlayers;
            int rawDescId  = rs1.getInt("DESCRIPTION_I18N_ID");
            descI18nId     = rs1.wasNull() ? (int?)null : rawDescId;

            string? sysImgRaw  = rs1.getString("SYSTEM_IMAGE_PATH");
            string? coverImgRaw = rs1.getString("COVER_IMAGE_PATH");
            systemImagePath    = BuildSystemImagePath(sysImgRaw, romStationPath, systemName ?? string.Empty);

            // Jaquette directement depuis GAME.GRAPHIC_IMAGE_ID → IMAGE.PATH
            if (!string.IsNullOrEmpty(coverImgRaw))
            {
                coverPath   = SystemPath.Combine(romStationPath, "app", coverImgRaw);
                coverExists = SystemFile.Exists(coverPath);
            }

            rs1.close(); ps1.close();

            // ── Requête 2 : description avec fallback ─────────────────────
            string? description        = null;
            bool    descriptionIsFallback = false;

            if (descI18nId.HasValue)
            {
                const string sqlDesc = """
                    SELECT loc.TAG, t.STRING
                    FROM APP."TRANSLATION" t
                    JOIN APP.LOCALE loc ON loc.ID = t.LOCALE_ID
                    WHERE t.I18N_ID = ?
                    """;

                var ps2 = conn.prepareStatement(sqlDesc);
                ps2.setInt(1, descI18nId.Value);
                var rs2 = ps2.executeQuery();

                string? descLocale = null;
                string? descEn     = null;

                while (rs2.next())
                {
                    string tag  = rs2.getString("TAG") ?? string.Empty;
                    string text = rs2.getString("STRING") ?? string.Empty;
                    if (tag == locale) descLocale = text;
                    if (tag == "en")   descEn     = text;
                }

                rs2.close(); ps2.close();

                if (descLocale != null)
                {
                    description = descLocale;
                }
                else if (descEn != null)
                {
                    description        = descEn;
                    descriptionIsFallback = locale != "en";
                }
            }

            // ── Requête 3 : genres avec fallback par genre ─────────────────
            const string sqlGenres = """
                SELECT gen.NAME_I18N_ID
                FROM APP.GAME_GENRE gg
                JOIN APP.GENRE gen ON gen.ID = gg.GENRE_ID
                WHERE gg.GAME_ID = ?
                """;

            var ps3 = conn.prepareStatement(sqlGenres);
            ps3.setInt(1, gameId);
            var rs3 = ps3.executeQuery();

            var genreI18nIds = new List<int>();
            while (rs3.next())
                genreI18nIds.Add(rs3.getInt(1));
            rs3.close(); ps3.close();

            var    genres          = new List<string>();
            bool   genresFallback  = false;

            const string sqlGenreName = """
                SELECT loc.TAG, t.STRING
                FROM APP."TRANSLATION" t
                JOIN APP.LOCALE loc ON loc.ID = t.LOCALE_ID
                WHERE t.I18N_ID = ?
                """;

            foreach (int i18nId in genreI18nIds)
            {
                var ps3b = conn.prepareStatement(sqlGenreName);
                ps3b.setInt(1, i18nId);
                var rs3b = ps3b.executeQuery();

                string? nameLocale = null;
                string? nameEn     = null;

                while (rs3b.next())
                {
                    string tag  = rs3b.getString("TAG")    ?? string.Empty;
                    string text = rs3b.getString("STRING") ?? string.Empty;
                    if (tag == locale) nameLocale = text;
                    if (tag == "en")   nameEn     = text;
                }

                rs3b.close(); ps3b.close();

                if (nameLocale != null)
                {
                    genres.Add(nameLocale);
                }
                else if (nameEn != null)
                {
                    genres.Add(nameEn);
                    if (locale != "en") genresFallback = true;
                }
            }

            genres.Sort(StringComparer.OrdinalIgnoreCase);

            // ── Requête 4 : langues du jeu ────────────────────────────────
            // La langue dont l'ID Derby correspond à la locale demandée (en=1, fr=2) est placée en tête.
            const string sqlLangs = """
                SELECT l.ID, l.NAME_I18N_ID, i.PATH AS FLAG_PATH
                FROM APP.GAME_LANGUAGE gl
                JOIN APP.LANGUAGE l    ON l.ID = gl.LANGUAGE_ID
                LEFT JOIN APP.IMAGE i  ON i.ID = l.GRAPHIC_IMAGE_ID
                WHERE gl.GAME_ID = ?
                ORDER BY l.ID
                """;

            var ps4 = conn.prepareStatement(sqlLangs);
            ps4.setInt(1, gameId);
            var rs4 = ps4.executeQuery();

            // Récupération des lignes brutes
            var langRows = new List<(int id, int nameI18nId, string? flagRaw)>();
            while (rs4.next())
            {
                langRows.Add((
                    rs4.getInt("ID"),
                    rs4.getInt("NAME_I18N_ID"),
                    rs4.getString("FLAG_PATH")
                ));
            }
            rs4.close(); ps4.close();

            // ID Derby de la langue "en" = 1, "fr" = 2
            int localeFirstId = locale == "fr" ? 2 : 1;

            // Tri : locale en tête, puis ID croissant (déjà trié par Derby)
            langRows.Sort((a, b) =>
            {
                bool aFirst = a.id == localeFirstId;
                bool bFirst = b.id == localeFirstId;
                if (aFirst != bFirst) return aFirst ? -1 : 1;
                return a.id.CompareTo(b.id);
            });

            const string sqlLangName = """
                SELECT loc.TAG, t.STRING
                FROM APP."TRANSLATION" t
                JOIN APP.LOCALE loc ON loc.ID = t.LOCALE_ID
                WHERE t.I18N_ID = ?
                """;

            var languages = new List<GameLanguageInfo>();

            foreach (var (langId, nameI18nId, flagRaw) in langRows)
            {
                var ps4b = conn.prepareStatement(sqlLangName);
                ps4b.setInt(1, nameI18nId);
                var rs4b = ps4b.executeQuery();

                string? nameLocale = null;
                string? nameEn     = null;

                while (rs4b.next())
                {
                    string tag  = rs4b.getString("TAG")    ?? string.Empty;
                    string text = rs4b.getString("STRING") ?? string.Empty;
                    if (tag == locale) nameLocale = text;
                    if (tag == "en")   nameEn     = text;
                }

                rs4b.close(); ps4b.close();

                string langName = nameLocale ?? nameEn ?? string.Empty;

                string? flagPath  = null;
                bool    flagExists = false;
                if (!string.IsNullOrEmpty(flagRaw))
                {
                    flagPath  = SystemPath.Combine(romStationPath, "app", flagRaw);
                    flagExists = SystemFile.Exists(flagPath);
                }

                languages.Add(new GameLanguageInfo
                {
                    Id            = langId,
                    Name          = langName,
                    FlagImagePath = flagPath,
                    FlagExists    = flagExists,
                });
            }

            // ── Requête 5 : URL RomStation (lien EXTERNAL=false) ──────────
            string? romStationUrl = null;

            const string sqlUrl = """
                SELECT l.LOCATION
                FROM APP.GAME_LINK gl
                JOIN APP.LINK l ON l.ID = gl.LINK_ID
                WHERE gl.GAME_ID = ?
                  AND l."EXTERNAL" = false
                FETCH FIRST 1 ROWS ONLY
                """;

            var ps5 = conn.prepareStatement(sqlUrl);
            ps5.setInt(1, gameId);
            var rs5 = ps5.executeQuery();

            if (rs5.next())
                romStationUrl = rs5.getString("LOCATION");

            rs5.close(); ps5.close();

            // ── Construction du résultat ───────────────────────────────────
            return new GameDetail
            {
                Id                   = gameId,
                Title                = title,
                Year                 = year,
                Players              = players,
                DeveloperName        = developerName,
                PublisherName        = publisherName,
                SystemName           = systemName ?? string.Empty,
                SystemImagePath      = systemImagePath,
                CoverPath            = coverPath,
                CoverExists          = coverExists,
                Directory            = gameDirectory,
                Description          = description,
                Genres               = genres.AsReadOnly(),
                Languages            = languages.AsReadOnly(),
                RomStationUrl        = romStationUrl,
                RequestedLocale      = locale,
                DescriptionIsFallback = descriptionIsFallback,
                GenresHasFallback    = genresFallback,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DerbyService] GetGameDetail ID={gameId} erreur : {ex.Message}");
            return null;
        }
    }

    // ── Helpers chemins ───────────────────────────────────────────────────

    /// <summary>
    /// Construit le chemin absolu vers la jaquette d'un jeu à partir de son répertoire Derby.
    /// GAME_FILE.DIRECTORY contient le chemin vers le fichier ROM (y compris le nom du fichier et un sous-dossier disque).
    /// Deux remontées permettent d'atteindre le dossier racine du jeu où se trouve images\cover.png.
    /// Retourne null si le répertoire est vide ou si les remontées échouent.
    /// </summary>
    private static string? BuildCoverPath(string? gameDirectory, string romStationPath)
    {
        if (string.IsNullOrEmpty(gameDirectory)) return null;

        // Derby peut stocker des chemins avec deux-points (lettres de lecteur Windows) ;
        // on normalise avant de manipuler le chemin.
        string dir = gameDirectory.Replace(':', '-');

        // Première remontée : dossier disque → dossier du jeu
        dir = SystemPath.GetDirectoryName(dir) ?? string.Empty;
        if (string.IsNullOrEmpty(dir)) return null;

        // Deuxième remontée : supprime un niveau de sous-dossier supplémentaire si présent
        dir = SystemPath.GetDirectoryName(dir) ?? string.Empty;
        if (string.IsNullOrEmpty(dir)) return null;

        return SystemPath.Combine(romStationPath, "app", dir, "images", "cover.png");
    }

    /// <summary>
    /// Résout le chemin d'icône d'un système selon 4 règles de priorité décroissante.
    /// Règle 1 : Windows → icône embarquée (win.png RomStation est blanc transparent).
    /// Règle 2 : macOS   → icône embarquée (osx.png RomStation est blanc transparent).
    /// Règle 3 : icône Derby si le fichier physique existe.
    /// Règle 4 : icône générique embarquée (fallback garanti, ne retourne jamais null).
    /// </summary>
    private static string BuildSystemImagePath(string? derbyImagePath, string romStationPath, string systemName)
    {
        if (string.Equals(systemName, "Windows", StringComparison.OrdinalIgnoreCase))
            return "pack://application:,,,/Resources/Icons/icon-system-windows.png";

        if (systemName.IndexOf("macos", StringComparison.OrdinalIgnoreCase) >= 0)
            return "pack://application:,,,/Resources/Icons/icon-system-apple.png";

        if (!string.IsNullOrEmpty(derbyImagePath))
        {
            string fullPath = SystemPath.Combine(romStationPath, "app", derbyImagePath);
            if (SystemFile.Exists(fullPath))
                return fullPath;
        }

        return "pack://application:,,,/Resources/Icons/icon-system-default.png";
    }

    // ── Connexion JDBC ────────────────────────────────────────────────────

    /// <summary>
    /// Shutdown propre de la base Derby embarquée pour libérer le verrou db.lck.
    /// Indispensable avant de supprimer/remplacer le dossier de la copie locale.
    /// Derby lève systématiquement une SQLException sur un shutdown — c'est normal et attendu
    /// (code SQL 08006 = "Database shutdown successfully"). On l'attrape et on ignore.
    /// </summary>
    public void ShutdownDatabase(string dbPath)
    {
        try
        {
            java.lang.Class.forName("org.apache.derby.jdbc.EmbeddedDriver");
            string jdbcUrl = $"jdbc:derby:{dbPath.Replace('\\', '/')};shutdown=true";
            DriverManager.getConnection(jdbcUrl);
        }
        catch (SQLException)
        {
            // Comportement normal de Derby sur un shutdown — aucun problème.
        }
        catch (Exception ex)
        {
            // Autre erreur (ex : base déjà shutdown) — on log en debug mais on n'échoue pas :
            // la suite de la sync essaiera quand même la suppression du dossier.
            Debug.WriteLine($"[DerbyService] ShutdownDatabase warning: {ex.Message}");
        }
    }

    /// <summary>
    /// Ouvre et retourne une connexion JDBC vers la base Derby au chemin indiqué.
    /// Enregistre le driver EmbeddedDriver d'IKVM avant la première connexion.
    /// Retourne null et logue l'erreur si la connexion échoue.
    /// </summary>
    private static Connection? OpenConnection(string dbPath)
    {
        try
        {
            // Enregistrement du driver Derby embarqué — nécessaire avec IKVM
            java.lang.Class.forName("org.apache.derby.jdbc.EmbeddedDriver");
            DriverManager.registerDriver(new EmbeddedDriver());

            // Derby attend des slashes Unix dans l'URL JDBC (pas de backslashes Windows)
            string jdbcUrl = $"jdbc:derby:{dbPath.Replace('\\', '/')}";
            return DriverManager.getConnection(jdbcUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DerbyService] Impossible d'ouvrir la connexion Derby : {ex.Message}");
            return null;
        }
    }
}
