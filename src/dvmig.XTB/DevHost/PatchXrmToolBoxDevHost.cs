using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PatchXrmToolBoxDevHost
{
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: PatchXrmToolBoxDevHost <path-to-XrmToolBox.exe>");
            return 2;
        }

        var path = args[0];
        var backup = path + ".original";
        if (!System.IO.File.Exists(backup))
        {
            System.IO.File.Copy(path, backup, overwrite: false);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(System.IO.Path.GetDirectoryName(path));

        using (var module = ModuleDefinition.ReadModule(path, new ReaderParameters { AssemblyResolver = resolver, ReadWrite = true }))
        {
            var newFormType = module.Types.First(t => t.FullName == "XrmToolBox.New.NewForm");

            PatchTaskBool(module, newFormType.Methods.First(m => m.Name == "LoadStore" && !m.HasParameters), false);
            PatchCompletedTask(module, newFormType.Methods.First(m => m.Name == "LaunchVersionCheck" && !m.HasParameters));
            PatchCompletedTask(module, newFormType.Methods.First(m => m.Name == "CheckForConnectionControlsUpdate" && m.Parameters.Count == 1));
            PatchVoid(newFormType.Methods.First(m => m.Name == "PrepareCategories" && !m.HasParameters));
            PatchNewFormLoad(module, newFormType.Methods.First(m => m.Name == "NewForm_Load" && m.Parameters.Count == 2));

            var pluginFormType = module.Types.First(t => t.FullName == "XrmToolBox.New.PluginsForm2");
            PatchVoid(pluginFormType.Methods.First(m => m.Name == "DisplayCategories" && m.Parameters.Count == 1));

            var announcementType = module.Types.First(t => t.FullName == "XrmToolBox.Announcement.AnnouncementManager");
            PatchVoid(announcementType.Methods.First(m => m.Name == "Display" && m.Parameters.Count == 1));
            PatchNull(announcementType.Methods.First(m => m.Name == "GetItemToDisplay" && !m.HasParameters));

            var appInsightsType = module.Types.First(t => t.FullName == "AppInsights");
            PatchVoid(appInsightsType.Methods.First(m => m.Name == "SendToAi" && m.Parameters.Count == 2));

            module.Write();
        }

        PatchToolLibrary(path, resolver);

        Console.WriteLine("Patched XrmToolBox dev host. Backup: " + backup);
        return 0;
    }

    private static void PatchToolLibrary(string xrmToolBoxPath, IAssemblyResolver resolver)
    {
        var toolLibraryPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(xrmToolBoxPath), "XrmToolBox.ToolLibrary.dll");
        if (!System.IO.File.Exists(toolLibraryPath))
        {
            return;
        }

        var backup = toolLibraryPath + ".original";
        if (!System.IO.File.Exists(backup))
        {
            System.IO.File.Copy(toolLibraryPath, backup, overwrite: false);
        }

        using (var module = ModuleDefinition.ReadModule(toolLibraryPath, new ReaderParameters { AssemblyResolver = resolver, ReadWrite = true }))
        {
            var toolLibraryType = module.Types.First(t => t.FullName == "XrmToolBox.ToolLibrary.ToolLibrary");
            PatchConstructor(module, toolLibraryType.Methods.First(m => m.Name == ".ctor" && m.Parameters.Count == 2));
            PatchCompletedTask(module, toolLibraryType.Methods.First(m => m.Name == "LoadTools" && m.Parameters.Count == 1));
            module.Write();
        }
    }

    private static void PatchTaskBool(ModuleDefinition module, MethodDefinition method, bool value)
    {
        var taskFromResult = typeof(System.Threading.Tasks.Task)
            .GetMethods()
            .First(m => m.Name == "FromResult" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(bool));

        Reset(method);
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(taskFromResult)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchCompletedTask(ModuleDefinition module, MethodDefinition method)
    {
        var completedTaskGetter = typeof(System.Threading.Tasks.Task).GetProperty("CompletedTask").GetGetMethod();
        Reset(method);
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Call, module.ImportReference(completedTaskGetter)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchVoid(MethodDefinition method)
    {
        Reset(method);
        method.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
    }

    private static void PatchNull(MethodDefinition method)
    {
        Reset(method);
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchConstructor(ModuleDefinition module, MethodDefinition method)
    {
        var objectCtor = typeof(object).GetConstructor(Type.EmptyTypes);
        Reset(method);
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(objectCtor)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void PatchNewFormLoad(ModuleDefinition module, MethodDefinition method)
    {
        var newFormType = module.Types.First(t => t.FullName == "XrmToolBox.New.NewForm");
        var pluginsFormField = newFormType.Fields.First(f => f.Name == "pluginsForm");
        var dockPanelField = newFormType.Fields.First(f => f.Name == "dpMain");

        var dockingAssemblyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(module.FileName), "WeifenLuo.WinFormsUI.Docking.dll");
        var dockingAssembly = System.Reflection.Assembly.LoadFrom(dockingAssemblyPath);
        var dockContentType = dockingAssembly.GetType("WeifenLuo.WinFormsUI.Docking.DockContent");
        var dockPanelType = dockingAssembly.GetType("WeifenLuo.WinFormsUI.Docking.DockPanel");
        var dockStateType = dockingAssembly.GetType("WeifenLuo.WinFormsUI.Docking.DockState");
        var dockShow = module.ImportReference(dockContentType.GetMethod("Show", new[] { dockPanelType, dockStateType }));
        var dockContentReference = module.ImportReference(dockContentType);

        var closeForm = module.Types
            .First(t => t.FullName == "XrmToolBox.Forms.WelcomeDialog")
            .Methods.First(m => m.Name == "CloseForm" && !m.HasParameters);
        var setOpacity = typeof(System.Windows.Forms.Form).GetProperty("Opacity").GetSetMethod();
        var bringToTop = newFormType.Methods.First(m => m.Name == "BringToTop" && !m.HasParameters);
        var checkEarlyBound = newFormType.Methods.First(m => m.Name == "CheckForEarlyBoundEntities" && !m.HasParameters);

        Reset(method);
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, pluginsFormField));
        il.Append(il.Create(OpCodes.Castclass, dockContentReference));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, dockPanelField));
        il.Append(il.Create(OpCodes.Ldc_I4_6));
        il.Append(il.Create(OpCodes.Callvirt, dockShow));
        il.Append(il.Create(OpCodes.Call, closeForm));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_R8, 100.0));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(setOpacity)));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, bringToTop));
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Call, checkEarlyBound));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void Reset(MethodDefinition method)
    {
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.Instructions.Clear();
        method.Body.InitLocals = false;
    }
}
