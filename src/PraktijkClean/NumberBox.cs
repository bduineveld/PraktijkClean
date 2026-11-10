using System.Drawing;
using System.Windows.Forms;

namespace PraktijkClean;

/// <summary>
/// Getalvak dat goed meeschaalt met de Windows-schaal. In .NET Framework wordt de marge van een gewone
/// NumericUpDown bij hoge schermschaal (bv. 200%) bij elke schaalstap opnieuw vermenigvuldigd
/// (3 px werd 768 px), waardoor andere onderdelen van het venster buiten beeld vielen.
/// </summary>
internal sealed class NumberBox : NumericUpDown
{
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        // Standaardmarge van 3 px bij 100%, eenmalig geschaald naar de huidige schermschaal.
        Margin = new Padding(UiFont.Px(3));
    }
}
