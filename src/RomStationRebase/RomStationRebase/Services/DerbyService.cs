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
            string? sysImageRaw     = rs.getString("SYSTEM_IMAGE_PATH");
            string? sysImagePath    = BuildSystemImagePath(sysImageRaw, romStationPath);
            string? coverPath       = BuildCoverPath(gameDirectory, romStationPath);
            string  gameName        = rs.getString("GAME_NAME") ?? string.Empty;

            if (sysImagePath is null)
                Debug.WriteLine($"[DerbyService] SystemImagePath null pour le jeu : {gameName} (SYSTEM_IMAGE_PATH brut = '{sysImageRaw}')");

            games.Add(new GameRecord
            {
                Id              = rs.getInt("ID"),
                Title           = gameName,
                SystemName      = rs.getString("SYSTEM_NAME")       ?? string.Empty,
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
            string? imagePath    = BuildSystemImagePath(imageRaw, romStationPath);

            if (imagePath is null)
                Debug.WriteLine($"[DerbyService] ImagePath null pour le système : {name} (IMAGE_PATH brut = '{imageRaw}')");

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
    /// Construit le chemin absolu vers l'image d'un système à partir du PATH Derby.
    /// Retourne null si le chemin Derby est vide.
    /// </summary>
    private static string? BuildSystemImagePath(string? derbyImagePath, string romStationPath)
    {
        if (string.IsNullOrEmpty(derbyImagePath)) return null;
        return SystemPath.Combine(romStationPath, "app", derbyImagePath);
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
