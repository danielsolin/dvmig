using System.ComponentModel.Composition;
using System.Collections.Specialized;
using System.Windows.Forms;
using System.Drawing;

using Microsoft.Xrm.Sdk;
using McTools.Xrm.Connection;

using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

using dvmig.Core.Interfaces;
using Label = System.Windows.Forms.Label;

namespace dvmig.XTB
{
   [Export(typeof(IXrmToolBoxPlugin))]
   [ExportMetadata("Name", "Dataverse Migration (dvmig)")]
   [ExportMetadata("SmallImageBase64", null)]
   [ExportMetadata("BigImageBase64", null)]
   [ExportMetadata("BackgroundColor", "White")]
   [ExportMetadata("PrimaryFontColor", "Black")]
   [ExportMetadata("SecondaryFontColor", "Gray")]
   [ExportMetadata(
      "Description",
      "Synchronize data between environments with audit preservation."
   )]
   public class DvmigPlugin : PluginBase
   {
      public override IXrmToolBoxPluginControl GetControl()
      {
         return new MainControl();
      }
   }

   public class MainControl : MultipleConnectionsPluginControlBase
   {
      private readonly Label _lblTarget;
      private readonly Label _lblSource;
      private readonly Button _btnSelectSource;
      private readonly FlowLayoutPanel _layout;

      private IDataverseProvider? _sourceProvider;
      private IDataverseProvider? _targetProvider;

      public MainControl()
      {
         _layout = new FlowLayoutPanel
         {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(20)
         };

         _lblTarget = new Label
         {
            Text = "Target: Not Connected",
            AutoSize = true,
            Font = new Font(
               FontFamily.GenericSansSerif,
               10,
               FontStyle.Bold
            )
         };

         _lblSource = new Label
         {
            Text = "Source: Not Connected",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
         };

         _btnSelectSource = new Button
         {
            Text = "Select Source Environment",
            Width = 200,
            Height = 30,
            Margin = new Padding(0, 10, 0, 0)
         };
         _btnSelectSource.Click += (s, e) => AddAdditionalOrganization();

         _layout.Controls.Add(_lblTarget);
         _layout.Controls.Add(_lblSource);
         _layout.Controls.Add(_btnSelectSource);

         Controls.Add(_layout);
      }

      public override void UpdateConnection(
         IOrganizationService newService,
         ConnectionDetail detail,
         string actionName,
         object parameter
      )
      {
         base.UpdateConnection(newService, detail, actionName, parameter);

         _targetProvider = new Providers.XrmToolBoxDataProvider(
            newService,
            detail.ConnectionName
         );

         _lblTarget.Text = $"Target: {detail.ConnectionName}";
      }

      protected override void ConnectionDetailsUpdated(
         NotifyCollectionChangedEventArgs e
      )
      {
         if (e.Action == NotifyCollectionChangedAction.Add
               && e.NewItems != null)
         {
            foreach (ConnectionDetail detail in e.NewItems)
            {
               var service = detail.GetCrmServiceClient();
               _sourceProvider = new Providers.XrmToolBoxDataProvider(
                  service,
                  detail.ConnectionName
               );

               _lblSource.Text = $"Source: {detail.ConnectionName}";
               break;
            }
         }
      }
   }
}
