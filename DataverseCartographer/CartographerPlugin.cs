using System.ComponentModel.Composition;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace DataverseCartographer
{
    /// <summary>
    /// Point d'entrée MEF du plugin « Dataverse Cartographer » pour XrmToolBox.
    /// </summary>
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Dataverse Cartographer"),
        ExportMetadata("Description", "Map your org's tables and relationships: interactive graph (1:N, N:1, N:N), filters by solution / publisher, neighborhood exploration, cascade behaviors, table cards, PNG / SVG / Mermaid / PlantUML / Excel export. English / French."),
        // Image 32x32
        ExportMetadata("SmallImageBase64", PluginIcons.Small),
        // Image 80x80
        ExportMetadata("BigImageBase64", PluginIcons.Big),
        ExportMetadata("BackgroundColor", "orange"),
        ExportMetadata("PrimaryFontColor", "white"),
        ExportMetadata("SecondaryFontColor", "Black")]
    public class CartographerPlugin : PluginBase, IPayPalPlugin
    {
        /// <summary>Donation through the XrmToolBox PayPal button (same account as Entity Fields Analyser).</summary>
        public string DonationDescription => "Dataverse Cartographer";

        public string EmailAccount => "hamzamraoui11@gmail.com";

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new CartographerControl();
        }
    }
}
