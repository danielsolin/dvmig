namespace dvmig.Core.Provisioning
{
   internal static class PluginAssemblyPathResolver
   {
      internal static string Resolve(
         string? pluginAssemblyPath,
         string baseDirectory,
         string coreAssemblyLocation
      )
      {
         if (!string.IsNullOrEmpty(pluginAssemblyPath))
            return pluginAssemblyPath!;

         var candidates = new List<string>();
         var coreAssemblyDirectory = Path.GetDirectoryName(
            coreAssemblyLocation
         );

         // XrmToolBox loads dvmig.Core and its dependencies from the
         // plugin dependency directory, not from the host directory.
         if (!string.IsNullOrEmpty(coreAssemblyDirectory))
            candidates.Add(Path.Combine(
               coreAssemblyDirectory,
               Shared.SystemConstants.AppConstants.PluginAssemblyName
            ));

         // The CLI and local debug builds normally place the plugin next
         // to the application, so keep the application directory as the
         // next fallback.
         candidates.Add(Path.Combine(
            baseDirectory,
            Shared.SystemConstants.AppConstants.PluginAssemblyName
         ));

         // Fallback for development if the project output is separate.
         candidates.Add(Path.Combine(
            baseDirectory,
            "..",
            "..",
            "..",
            "..",
            Shared.SystemConstants.AppConstants.PluginName,
            "bin",
            "Debug",
            "netstandard2.0",
            Shared.SystemConstants.AppConstants.PluginAssemblyName
         ));

         return candidates.FirstOrDefault(File.Exists) ?? candidates.Last();
      }
   }
}
