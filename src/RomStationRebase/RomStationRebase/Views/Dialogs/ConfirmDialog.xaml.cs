using System.Windows;
using System.Windows.Input;

namespace RomStationRebase.Views.Dialogs;

/// <summary>
/// Dialog de confirmation générique — titre, message, bouton primaire et secondaire.
/// La propriété Result indique si l'utilisateur a cliqué sur le bouton primaire (true) ou non (false).
/// </summary>
public enum ConfirmChoice { None, Primary, Secondary, Tertiary }

public partial class ConfirmDialog : Window
{
    /// <summary>True si le bouton primaire a été cliqué, false sinon.</summary>
    public bool Result { get; private set; }

    /// <summary>Bouton cliqué. None si la fenêtre a été fermée autrement : à traiter comme un renoncement.</summary>
    public ConfirmChoice Choice { get; private set; } = ConfirmChoice.None;

    /// <param name="title">Texte du header.</param>
    /// <param name="message">Corps du message (supporte le retour à la ligne).</param>
    /// <param name="primaryLabel">Libellé du bouton primaire (action confirmée).</param>
    /// <param name="secondaryLabel">Libellé du bouton secondaire (annulation).</param>
    /// <param name="secondaryLabel">Libellé du bouton secondaire — null ou vide pour l'masquer.</param>
    /// <param name="tertiaryLabel">Libellé d'un troisième bouton, à gauche des deux autres — null pour ne pas l'afficher.</param>
    public ConfirmDialog(string title, string message, string primaryLabel, string? secondaryLabel = null,
                         string? tertiaryLabel = null)
    {
        InitializeComponent();
        TitleText.Text        = title;
        MessageText.Text      = message;
        PrimaryButton.Content = primaryLabel;

        if (string.IsNullOrEmpty(secondaryLabel))
            SecondaryButton.Visibility = Visibility.Collapsed;
        else
            SecondaryButton.Content = secondaryLabel;

        if (!string.IsNullOrEmpty(tertiaryLabel))
        {
            TertiaryButton.Content    = tertiaryLabel;
            TertiaryButton.Visibility = Visibility.Visible;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        Choice = ConfirmChoice.Primary;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Choice = ConfirmChoice.Secondary;
        Close();
    }

    private void TertiaryButton_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Choice = ConfirmChoice.Tertiary;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();
}
