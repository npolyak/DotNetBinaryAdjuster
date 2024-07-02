using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;

namespace DotNetBinaryAdjuster.Tasks
{
    public class PublicizeInternals : Task
    {
        private readonly AssemblyResolver _resolver = new AssemblyResolver();

        [Required]
        public ITaskItem[] SourceReferences { get; set; }

        [Required]
        public ITaskItem[] AssemblyNames { get; set; }

        [Required]
        public string IntermediateOutputPath { get; set; }

        [Required]
        public string GeneratedCodeFilePath { get; set; }

        public ITaskItem[] ExcludeTypeNames { get; set; }

        public ITaskItem[] ExposeAllMethodsTypeNames { get; set; }

        public ITaskItem[] VirtualizeMethodNames { get; set; }

        public override bool Execute()
        {
            if (SourceReferences == null) throw new ArgumentNullException(nameof(SourceReferences));

            var assemblyNames = 
                new HashSet<string>
                (
                    AssemblyNames.Select(t => t.ItemSpec), StringComparer.OrdinalIgnoreCase);

            if (assemblyNames.Count == 0)
            {
                return true;
            }

            var excludedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ExcludeTypeNames != null)
            {
                excludedTypeNames = 
                    new HashSet<string>
                    (
                        ExcludeTypeNames.Select(t => t.ItemSpec), StringComparer.OrdinalIgnoreCase);
            }

            HashSet<string> exposeAllMethodsTypeNames = null;
            if (ExposeAllMethodsTypeNames != null)
            {
                exposeAllMethodsTypeNames =
                    new HashSet<string>
                    (
                        ExposeAllMethodsTypeNames.Select(t => t.ItemSpec));
            }
            else
            {
                exposeAllMethodsTypeNames = new HashSet<string>();
            }

            Dictionary<string, ClassMethods> virtualizedMethodsDictionary =
                new Dictionary<string, ClassMethods>();

            if (VirtualizeMethodNames?.Any() == true)
            {
                foreach(string classAndMethodName in VirtualizeMethodNames.Select(t => t.ItemSpec))
                {
                    Log.LogMessageFromText($"\t{classAndMethodName}", MessageImportance.High);

                    int separatorIdx = classAndMethodName.IndexOf(':');

                    if (separatorIdx == -1)
                    {
                        string errorMsg = $"ERROR: There is no separator ':' in the class/method name '{classAndMethodName}'";
                        Log.LogMessageFromText(errorMsg, MessageImportance.High);
                        throw new Exception(errorMsg);
                    }

                    string className = classAndMethodName.Substring(0, separatorIdx).Trim();

                    if (className == string.Empty)
                    {
                        string errorMsg = $"ERROR: Class name in {classAndMethodName} cannot be empty";
                        Log.LogMessageFromText(errorMsg, MessageImportance.High);
                        throw new Exception(errorMsg);
                    }

                    string methodName = classAndMethodName.Substring(separatorIdx + 1).Trim();

                    if (methodName == string.Empty)
                    {
                        string errorMsg = $"ERROR: Method name in {classAndMethodName} cannot be empty";
                        Log.LogMessageFromText(errorMsg, MessageImportance.High);
                        throw new Exception(errorMsg);
                    }

                    if (!virtualizedMethodsDictionary.TryGetValue(className, out ClassMethods classMethods))
                    {
                        classMethods = new ClassMethods(className);
                        virtualizedMethodsDictionary[className] = classMethods;
                    }

                    Log.LogMessageFromText($"ClassName: '{classMethods.ClassName}'", MessageImportance.High);
                    Log.LogMessageFromText($"MethodName: '{methodName}'", MessageImportance.High);
                    classMethods.MethodNames.Add(methodName);
                }
            }


            var targetPath = IntermediateOutputPath;
            Directory.CreateDirectory(targetPath);

            GenerateAttributes(targetPath, assemblyNames);

            foreach (var assemblyPath in SourceReferences
                .Select(a => Path.GetDirectoryName(GetFullFilePath(targetPath, a.ItemSpec))))
            {
                _resolver.AddSearchDirectory(assemblyPath);
            }

            foreach (var assembly in SourceReferences)
            {
                var assemblyPath = GetFullFilePath(targetPath, assembly.ItemSpec);
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
                if (assemblyNames.Contains(assemblyName))
                {
                    var targetAssemblyPath = Path.Combine(targetPath, Path.GetFileName(assemblyPath));

                    CreatePublicAssembly(assemblyPath, targetAssemblyPath, excludedTypeNames, exposeAllMethodsTypeNames, virtualizedMethodsDictionary);
                    Log.LogMessageFromText("Created publicized assembly at " + targetAssemblyPath, MessageImportance.Normal);
                }
            }

            return true;
        }

        private void GenerateAttributes(string path, IEnumerable<string> assemblyNames)
        {
            var attributes = string.Join(Environment.NewLine,
                assemblyNames.Select(a => $@"[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo(""{a}"")]"));

            var content = attributes + @"

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
        }
    }
}";
            File.WriteAllText(GeneratedCodeFilePath, content);

            Log.LogMessageFromText("Generated IgnoresAccessChecksTo attributes", MessageImportance.Low);
        }

        private void CreatePublicAssembly
        (
            string source, 
            string target, 
            HashSet<string> excludedTypeNames,
            HashSet<string> allMethodsExposedTypeNames,
            Dictionary<string, ClassMethods> virtualizedMethodsDictionary)
        {
            //if (virtualizedMethodsDictionary == null)
            //{
            //    Log.LogMessageFromText($"ERROR: virtualizedMethodsDictionary is null", MessageImportance.High);
            //}
            //else
            //{
            //    Log.LogMessageFromText($"NOT AN ERROR: virtualizedMethodsDictionary is NOT null", MessageImportance.High);
            //}

            var assembly = AssemblyDefinition.ReadAssembly(source,
                new ReaderParameters { AssemblyResolver = _resolver });


            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.GetTypes().Where(type => !excludedTypeNames.Contains(type.FullName)))
                {
                    if (!type.IsNested && type.IsNotPublic)
                    {
                        type.IsPublic = true;
                    }
                    else if (type.IsNestedAssembly ||
                             type.IsNestedFamilyOrAssembly ||
                             type.IsNestedFamilyAndAssembly)
                    {
                        type.IsNestedPublic = true;
                    }

                    foreach (var field in type.Fields)
                    {
                        if (field.IsAssembly ||
                            field.IsFamilyOrAssembly ||
                            field.IsFamilyAndAssembly)
                        {
                            field.IsPublic = true;
                        }
                    }

                    if (!virtualizedMethodsDictionary.TryGetValue(type.FullName, out ClassMethods classMethodsToVirtualize))
                    {
                        classMethodsToVirtualize = null;
                    }

                    HashSet<string> typeMethodsToVirtualize =
                        classMethodsToVirtualize?.MethodNames ?? new HashSet<string>();

                    foreach (string methodName in typeMethodsToVirtualize)
                    {
                        Log.LogMessageFromText($"MethodName: '{methodName}'", MessageImportance.High);

                        MethodDefinition method = type.Methods.FirstOrDefault(m => m.Name == methodName);

                        if (method != null)
                        {
                            if (!method.IsFamily && !method.IsPublic)
                            {
                                Log.LogMessageFromText($"Method made Family: {method.Name}", MessageImportance.Normal);
                                method.IsFamily = true;
                            }
                            if (!method.IsVirtual)
                            {
                                Log.LogMessageFromText($"Method made Virtual: {method.Name}", MessageImportance.Normal);
                                method.IsVirtual = true;
                            }
                        }
                    }    
                    if (allMethodsExposedTypeNames.Contains(type.FullName))
                    {
                        foreach (var method in type.Methods)
                        {
                            if (!method.IsPublic && !method.HasOverrides && !method.IsVirtual)
                            {
                                method.IsPublic = true;
                            }
                        }
                    }
                    else
                    {
                        foreach (var method in type.Methods)
                        {
                            if ( (method.IsAssembly ||
                                  method.IsFamilyOrAssembly ||
                                   method.IsFamilyAndAssembly) && !method.HasOverrides && !method.IsVirtual)
                            {
                                method.IsPublic = true;
                            }
                        }
                    }
                }
            }

            assembly.Write(target);
        }

        private string GetFullFilePath(string basePath, string path) =>
            Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.Combine(basePath, path);

        private class AssemblyResolver : IAssemblyResolver
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public void AddSearchDirectory(string directory)
            {
                _directories.Add(directory);
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name)
            {
                return Resolve(name, new ReaderParameters());
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                var assembly = SearchDirectory(name, _directories, parameters);
                if (assembly != null)
                {
                    return assembly;
                }

                throw new AssemblyResolutionException(name);
            }

            public void Dispose()
            {
            }

            private AssemblyDefinition SearchDirectory(AssemblyNameReference name, IEnumerable<string> directories, ReaderParameters parameters)
            {
                var extensions = name.IsWindowsRuntime ? new[] { ".winmd", ".dll" } : new[] { ".exe", ".dll" };
                foreach (var directory in directories)
                {
                    foreach (var extension in extensions)
                    {
                        var file = Path.Combine(directory, name.Name + extension);
                        if (!File.Exists(file))
                            continue;
                        try
                        {
                            return GetAssembly(file, parameters);
                        }
                        catch (BadImageFormatException)
                        {
                        }
                    }
                }

                return null;
            }

            private AssemblyDefinition GetAssembly(string file, ReaderParameters parameters)
            {
                if (parameters.AssemblyResolver == null)
                    parameters.AssemblyResolver = this;

                return ModuleDefinition.ReadModule(file, parameters).Assembly;
            }
        }

        class ClassMethods
        {
            public string ClassName { get; }

            public HashSet<string> MethodNames { get; } = new HashSet<string>();

            public ClassMethods(string className)
            {
                ClassName = className;
            }
        }
    }
}
