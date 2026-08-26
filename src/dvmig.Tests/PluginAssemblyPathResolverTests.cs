using dvmig.Core.Provisioning;
using static dvmig.Core.Shared.SystemConstants;

namespace dvmig.Tests
{
   public class PluginAssemblyPathResolverTests
   {
      [Fact]
      public void Resolve_FindsPluginBesideLoadedCoreAssembly()
      {
         var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "dvmig-" + Guid.NewGuid().ToString("N")
         );
         var baseDirectory = Path.Combine(rootDirectory, "XrmToolBox");
         var dependencyDirectory = Path.Combine(
            baseDirectory,
            "Plugins",
            "dvmig.XTB"
         );
         var pluginPath = Path.Combine(
            dependencyDirectory,
            AppConstants.PluginAssemblyName
         );

         try
         {
            Directory.CreateDirectory(dependencyDirectory);
            File.WriteAllBytes(pluginPath, Array.Empty<byte>());

            var result = PluginAssemblyPathResolver.Resolve(
               null,
               baseDirectory,
               Path.Combine(dependencyDirectory, "dvmig.Core.dll")
            );

            Assert.Equal(pluginPath, result);
         }
         finally
         {
            if (Directory.Exists(rootDirectory))
               Directory.Delete(rootDirectory, true);
         }
      }
   }
}
