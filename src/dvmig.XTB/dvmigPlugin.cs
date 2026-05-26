using System.ComponentModel.Composition;
using dvmig.XTB.UI;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace dvmig.XTB
{
   #region DvmigPlugin
   [Export(typeof(IXrmToolBoxPlugin))]
   [ExportMetadata("Name", "Dataverse Migration (dvmig)")]
   [ExportMetadata("SmallImageBase64", PluginImages.SmallImageBase64)]
   [ExportMetadata("BigImageBase64", PluginImages.BigImageBase64)]
   [ExportMetadata("BackgroundColor", "White")]
   [ExportMetadata("PrimaryFontColor", "Black")]
   [ExportMetadata("SecondaryFontColor", "Gray")]
   [ExportMetadata(
      "Description",
      "Synchronize data between environments with audit preservation."
   )]
   public class DvmigPlugin : PluginBase, INoConnectionRequired
   {
      public override IXrmToolBoxPluginControl GetControl()
      {
         return new MainControl();
      }
   }
   #endregion
}
