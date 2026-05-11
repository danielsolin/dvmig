using System.ComponentModel.Composition;
using System.Windows.Forms;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using Label = System.Windows.Forms.Label;

namespace dvmig.XTB
{
   [Export(typeof(IXrmToolBoxPlugin))]
   [ExportMetadata("Name", "Dataverse Migration (dvmig)")]
   [ExportMetadata("Description", "Synchronize data between environments" +
      " with audit preservation.")]
   [ExportMetadata("SmallImageBase64", null)]
   [ExportMetadata("BigImageBase64", null)]
   [ExportMetadata("BackgroundColor", "White")]
   [ExportMetadata("PrimaryFontColor", "Black")]
   [ExportMetadata("SecondaryFontColor", "Gray")]
   public class dvmigPlugin : PluginBase
   {
      public override IXrmToolBoxPluginControl GetControl()
      {
         return new MainControl();
      }
   }

   public class MainControl : PluginControlBase
   {
      private Label _lblStatus;

      public MainControl()
      {
         _lblStatus = new Label
         {
            Text = "dvmig XrmToolBox Plugin Initialized",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter
         };
         Controls.Add(_lblStatus);
      }
   }
}
