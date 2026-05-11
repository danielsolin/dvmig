using System.Text;

namespace FileConcatenator
{
   /// <summary>
   /// Utility to concatenate all .cs files in the src directory 
   /// into a single dvmig.cs file.
   /// </summary>
   class Program
   {
      private static string _output = "dvmig.cs";

      static void Main(string[] args)
      {
         var rootDir = AppDomain.CurrentDomain.BaseDirectory;
         var srcDir = FindSrcDirectory(rootDir);

         if (srcDir == null)
         {
            Console.WriteLine("Could not find src directory.");

            return;
         }

         File.Delete(Path.Combine(srcDir, _output));

         var outputFilePath = Path.Combine(srcDir, _output);

         var csFiles = Directory.GetFiles(
               srcDir, 
               "*.cs", 
               SearchOption.AllDirectories
            )
            .Where(f =>
               f.Contains(
                  $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"
               ) == false &&
               f.Contains(
                  $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
               ) == false &&
               f.Contains("Tests") == false &&
               f.Contains("Plugin") == false &&
               f.EndsWith("FileConcatenator.cs") == false &&
               f.EndsWith(_output) == false
            )
            .OrderBy(f => f)
            .ToList();

         var allUsings = new HashSet<string>();
         var consolidatedCode = new StringBuilder();

         foreach (var file in csFiles)
            ProcessFile(file, allUsings, consolidatedCode, srcDir);

         var finalOutput = new StringBuilder();
         
         foreach (var u in allUsings.OrderBy(s => s))
            finalOutput.AppendLine(u);

         finalOutput.AppendLine();
         finalOutput.Append(consolidatedCode);

         File.WriteAllText(outputFilePath, finalOutput.ToString());
         
         Console.WriteLine(
            $"Successfully concatenated {csFiles.Count} files " +
            $"into {outputFilePath}"
         );
      }

      static string? FindSrcDirectory(string startDir)
      {
         var current = new DirectoryInfo(startDir);

         while (current != null)
         {
            if (current.Name == "src")
               return current.FullName;

            var subSrc = current.GetDirectories("src").FirstOrDefault();

            if (subSrc != null)
               return subSrc.FullName;

            current = current.Parent;
         }

         return null;
      }

      static void ProcessFile(
         string filePath, 
         HashSet<string> allUsings, 
         StringBuilder consolidatedCode,
         string srcDir
      )
      {
         var lines = File.ReadAllLines(filePath);
         var relativePath = Path.GetRelativePath(srcDir, filePath);

         consolidatedCode.AppendLine($"// --- Source: {relativePath} ---");

         foreach (var line in lines)
         {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
               allUsings.Add(trimmed);
            else
               consolidatedCode.AppendLine(line);
         }

         consolidatedCode.AppendLine();
         consolidatedCode.AppendLine();
      }
   }
}
