﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using AIDevGallery.Models;
using AIDevGallery.Samples;
using AIDevGallery.Telemetry.Events;
using Microsoft.Build.Construction;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace AIDevGallery.ProjectGenerator
{
    internal partial class Generator
    {
        private readonly string templatePath = Path.Join(Package.Current.InstalledLocation.Path, "ProjectGenerator", "Template");

        [GeneratedRegex(@"[^a-zA-Z0-9_]")]
        private static partial Regex SafeNameRegex();

        private static string ToSafeVariableName(string input)
        {
            // Replace invalid characters with an underscore
            string safeName = SafeNameRegex().Replace(input, "_");

            // Ensure the name does not start with a digit
            if (safeName.Length > 0 && char.IsDigit(safeName[0]))
            {
                safeName = "_" + safeName;
            }

            // If the name is empty or only contains invalid characters, return a default name
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = "MySampleApp";
            }

            return safeName;
        }

        internal Task<string> GenerateAsync(Sample sample, Dictionary<ModelType, (string CachedModelDirectoryPath, string ModelUrl)> models, bool copyModelLocally, string outputPath, CancellationToken cancellationToken)
        {
            var packageReferences = new List<(string PackageName, string? Version)>
            {
                ("Microsoft.WindowsAppSDK", null),
                ("Microsoft.Windows.SDK.BuildTools", null),
            };

            foreach (var nugetPackageReference in sample.NugetPackageReferences)
            {
                packageReferences.Add(new(nugetPackageReference, null));
            }

            return GenerateAsyncInternal(sample, models, copyModelLocally, packageReferences, outputPath, cancellationToken);
        }

        internal const string DotNetVersion = "net8.0";

        private async Task<string> GenerateAsyncInternal(Sample sample, Dictionary<ModelType, (string CachedModelDirectoryPath, string ModelUrl)> models, bool copyModelLocally, List<(string PackageName, string? Version)> packageReferences, string outputPath, CancellationToken cancellationToken)
        {
            var projectName = $"{sample.Name}Sample";
            string safeProjectName = ToSafeVariableName(projectName);
            string guid9 = Guid.NewGuid().ToString();
            string xmlEscapedPublisher = "MyTestPublisher";
            string xmlEscapedPublisherDistinguishedName = $"CN={xmlEscapedPublisher}";

            outputPath = Path.Join(outputPath, safeProjectName);
            var dirIndexCount = 1;
            while (Directory.Exists(outputPath))
            {
                outputPath = Path.Join(Path.GetDirectoryName(outputPath), $"{safeProjectName}_{dirIndexCount}");
                dirIndexCount++;
            }

            var modelTypes = sample.Model1Types.Concat(sample.Model2Types ?? Enumerable.Empty<ModelType>())
                    .Where(models.ContainsKey);

            if (copyModelLocally)
            {
                long sumTotalSize = 0;
                foreach (var modelType in modelTypes)
                {
                    if (!models.TryGetValue(modelType, out var modelInfo))
                    {
                        throw new ArgumentException($"Model type {modelType} not found in the models dictionary", nameof(models));
                    }

                    if (modelInfo.CachedModelDirectoryPath.Contains("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var cachedModelDirectoryAttributes = File.GetAttributes(modelInfo.CachedModelDirectoryPath);

                    if (cachedModelDirectoryAttributes.HasFlag(FileAttributes.Directory))
                    {
                        sumTotalSize += Directory.GetFiles(modelInfo.CachedModelDirectoryPath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                    }
                    else
                    {
                        sumTotalSize += new FileInfo(modelInfo.CachedModelDirectoryPath).Length;
                    }
                }

                var availableSpace = DriveInfo.GetDrives().First(d => d.RootDirectory.FullName == Path.GetPathRoot(outputPath)).AvailableFreeSpace;
                if (sumTotalSize > availableSpace)
                {
                    throw new IOException("Not enough disk space to copy the model files.");
                }
            }

            Directory.CreateDirectory(outputPath);

            bool addLllmTypes = false;
            Dictionary<ModelType, (string CachedModelDirectoryPath, string ModelUrl, bool IsSingleFile, string ModelPathStr, HardwareAccelerator HardwareAccelerator, PromptTemplate? ModelPromptTemplate)> modelInfos = [];
            string model1Id = string.Empty;
            string model2Id = string.Empty;
            foreach (var modelType in modelTypes)
            {
                if (!models.TryGetValue(modelType, out var modelInfo))
                {
                    throw new ArgumentException($"Model type {modelType} not found in the models dictionary", nameof(models));
                }

                PromptTemplate? modelPromptTemplate = null;
                HardwareAccelerator hardwareAccelerator = HardwareAccelerator.CPU;
                string modelId = string.Empty;
                bool isSingleFile = false;

                if (ModelTypeHelpers.ModelDetails.TryGetValue(modelType, out var modelDetails))
                {
                    modelPromptTemplate = modelDetails.PromptTemplate;
                    hardwareAccelerator = modelDetails.HardwareAccelerators.First();
                    modelId = modelDetails.Id;
                }
                else if (ModelTypeHelpers.ModelDetails.FirstOrDefault(mf => mf.Value.Url == modelInfo.ModelUrl) is var modelDetails2 && modelDetails2.Value != null)
                {
                    modelPromptTemplate = modelDetails2.Value.PromptTemplate;
                    if (modelPromptTemplate != null)
                    {
                        addLllmTypes = true;
                    }

                    hardwareAccelerator = modelDetails2.Value.HardwareAccelerators.First();
                    modelId = modelDetails2.Value.Id;
                }
                else if (ModelTypeHelpers.ApiDefinitionDetails.TryGetValue(modelType, out var apiDefinitionDetails))
                {
                    hardwareAccelerator = HardwareAccelerator.DML;
                    modelId = apiDefinitionDetails.Id;
                }

                string modelPathStr;

                if (copyModelLocally && !modelInfo.CachedModelDirectoryPath.Contains("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var modelPath = Path.GetFileName(modelInfo.CachedModelDirectoryPath);
                    var cachedModelDirectoryAttributes = File.GetAttributes(modelInfo.CachedModelDirectoryPath);

                    if (cachedModelDirectoryAttributes.HasFlag(FileAttributes.Directory))
                    {
                        isSingleFile = false;
                        var modelDirectory = Directory.CreateDirectory(Path.Join(outputPath, "Models", modelPath));
                        foreach (var file in Directory.GetFiles(modelInfo.CachedModelDirectoryPath, "*", SearchOption.AllDirectories))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var filePath = Path.Join(modelDirectory.FullName, Path.GetRelativePath(modelInfo.CachedModelDirectoryPath, file));
                            var directory = Path.GetDirectoryName(filePath);
                            if (directory != null && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            await CopyFileAsync(file, filePath, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        isSingleFile = true;
                        var modelDirectory = Directory.CreateDirectory(Path.Join(outputPath, "Models"));
                        await CopyFileAsync(modelInfo.CachedModelDirectoryPath, Path.Join(modelDirectory.FullName, modelPath), cancellationToken).ConfigureAwait(false);
                    }

                    modelPathStr = $"System.IO.Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, \"Models\", @\"{modelPath}\")";
                    modelInfo.CachedModelDirectoryPath = modelPath;
                }
                else
                {
                    modelPathStr = $"@\"{modelInfo.CachedModelDirectoryPath}\"";
                }

                modelInfos.Add(modelType, new(modelInfo.CachedModelDirectoryPath, modelInfo.ModelUrl, isSingleFile, modelPathStr, hardwareAccelerator, modelPromptTemplate));

                if (modelTypes.First() == modelType)
                {
                    model1Id = modelId;
                }
                else
                {
                    model2Id = modelId;
                }
            }

            SampleProjectGeneratedEvent.Log(sample.Id, model1Id, model2Id, copyModelLocally);

            string[] extensions = [".manifest", ".xaml", ".cs", ".appxmanifest", ".csproj", ".ico", ".png", ".json", ".pubxml"];

            // Get all files from the template directory with the allowed extensions
            var files = Directory.GetFiles(templatePath, "*.*", SearchOption.AllDirectories).Where(file => extensions.Any(file.EndsWith));

            var renames = new Dictionary<string, string>
            {
                { "Package-managed.appxmanifest", "Package.appxmanifest" },
                { "ProjectTemplate.csproj", $"{safeProjectName}.csproj" }
            };

            var className = await AddFilesFromSampleAsync(sample, packageReferences, safeProjectName, outputPath, addLllmTypes, modelInfos, cancellationToken);

            foreach (var file in files)
            {
                var relativePath = file[(templatePath.Length + 1)..];

                var fileName = Path.GetFileName(file);
                if (renames.TryGetValue(fileName, out var newName))
                {
                    relativePath = relativePath.Replace(fileName, newName);
                }

                var outputPathFile = Path.Join(outputPath, relativePath);

                // Create the directory if it doesn't exist
                var directory = Path.GetDirectoryName(outputPathFile);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // if image file, just copy
                if (Path.GetExtension(file) is ".ico" or ".png")
                {
                    File.Copy(file, outputPathFile);
                    continue;
                }
                else
                {
                    // Read the file
                    var content = await File.ReadAllTextAsync(file, cancellationToken);

                    // Replace the variables
                    content = content.Replace("$projectname$", projectName);
                    content = content.Replace("$safeprojectname$", safeProjectName);
                    content = content.Replace("$guid9$", guid9);
                    content = content.Replace("$XmlEscapedPublisherDistinguishedName$", xmlEscapedPublisherDistinguishedName);
                    content = content.Replace("$XmlEscapedPublisher$", xmlEscapedPublisher);
                    content = content.Replace("$DotNetVersion$", DotNetVersion);
                    content = content.Replace("$MainSamplePage$", className);

                    // Write the file
                    await File.WriteAllTextAsync(outputPathFile, content, cancellationToken);
                }
            }

            var csproj = Path.Join(outputPath, $"{safeProjectName}.csproj");

            // Add NuGet references
            if (packageReferences.Count > 0 || copyModelLocally)
            {
                var project = ProjectRootElement.Open(csproj);
                var itemGroup = project.AddItemGroup();

                foreach (var packageReference in packageReferences)
                {
                    var packageName = packageReference.PackageName;
                    var version = packageReference.Version;
                    var packageReferenceItem = itemGroup.AddItem("PackageReference", packageName);

                    if (packageName == "Microsoft.Windows.CsWin32")
                    {
                        packageReferenceItem.AddMetadata("PrivateAssets", "all", true);
                    }

                    var versionStr = version ?? PackageVersionHelpers.PackageVersions[packageName];
                    packageReferenceItem.AddMetadata("Version", versionStr, true);
                }

                if (copyModelLocally)
                {
                    var modelContentItemGroup = project.AddItemGroup();
                    foreach (var modelInfo in modelInfos)
                    {
                        if (modelInfo.Value.CachedModelDirectoryPath.Contains("file://", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (modelInfo.Value.IsSingleFile)
                        {
                            modelContentItemGroup.AddItem("Content", @$"Models\{modelInfo.Value.CachedModelDirectoryPath}");
                        }
                        else
                        {
                            modelContentItemGroup.AddItem("Content", @$"Models\{modelInfo.Value.CachedModelDirectoryPath}\**");
                        }
                    }
                }

                project.Save();
            }

            // Fix PublishProfiles. This shouldn't be necessary once the templates are fixed
            foreach (var file in Directory.GetFiles(outputPath, "*.pubxml", SearchOption.AllDirectories))
            {
                var pubxml = ProjectRootElement.Open(file);
                var firstPg = pubxml.PropertyGroups.FirstOrDefault();
                firstPg ??= pubxml.AddPropertyGroup();

                if (!firstPg.Children.Any(p => p.ElementName == "RuntimeIdentifier"))
                {
                    var runtimeIdentifier = Path.GetFileNameWithoutExtension(file).Split('-').Last();
                    firstPg.AddProperty("RuntimeIdentifier", $"win-{runtimeIdentifier}");
                }

                pubxml.Save();
            }

            // Styles
            List<string> styles = [];
            foreach (var file in Directory.GetFiles(outputPath, "*.xaml", SearchOption.TopDirectoryOnly))
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                if (!content.StartsWith("<ResourceDictionary", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                styles.Add(file);
            }

            if (styles.Count > 0)
            {
                var appXamlPath = Path.Join(outputPath, "App.xaml");
                var appXaml = await File.ReadAllTextAsync(appXamlPath, cancellationToken);
                appXaml = appXaml.Replace(
                    "                <!-- Other merged dictionaries here -->",
                    string.Join(Environment.NewLine, styles.Select(s => $"                <ResourceDictionary Source=\"{Path.GetFileName(s)}\" />")));
                await File.WriteAllTextAsync(appXamlPath, appXaml, cancellationToken);
            }

            return outputPath;
        }

        private string GetChatClientLoaderString(Sample sample, string modelPath, string promptTemplate)
        {
            if (!sample.SharedCode.Contains(SharedCodeEnum.GenAIModel))
            {
                return string.Empty;
            }

            return $"GenAIModel.CreateAsync({modelPath}, {promptTemplate})";
        }

        private static async Task CopyFileAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken)
        {
            using var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            using var destinationStream = new FileStream(destinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
            await sourceStream.CopyToAsync(destinationStream, 81920, cancellationToken).ConfigureAwait(false);
        }

        private static string EscapeNewLines(string str)
        {
            str = str
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
            return str;
        }

        private string GetPromptTemplateString(PromptTemplate? promptTemplate, int spaceCount)
        {
            if (promptTemplate == null)
            {
                return "null";
            }

            StringBuilder modelPromptTemplateSb = new();
            var spaces = new string(' ', spaceCount);
            modelPromptTemplateSb.AppendLine("new LlmPromptTemplate");
            modelPromptTemplateSb.Append(spaces);
            modelPromptTemplateSb.AppendLine("{");
            if (!string.IsNullOrEmpty(promptTemplate.System))
            {
                modelPromptTemplateSb.Append(spaces);
                modelPromptTemplateSb.AppendLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        """
                            System = "{0}",
                        """,
                        EscapeNewLines(promptTemplate.System)));
            }

            if (!string.IsNullOrEmpty(promptTemplate.User))
            {
                modelPromptTemplateSb.Append(spaces);
                modelPromptTemplateSb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        """
                            User = "{0}",
                        """,
                        EscapeNewLines(promptTemplate.User)));
            }

            if (!string.IsNullOrEmpty(promptTemplate.Assistant))
            {
                modelPromptTemplateSb.Append(spaces);
                modelPromptTemplateSb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        """
                            Assistant = "{0}",
                        """,
                        EscapeNewLines(promptTemplate.Assistant)));
            }

            if (promptTemplate.Stop != null && promptTemplate.Stop.Length > 0)
            {
                modelPromptTemplateSb.Append(spaces);
                var stopStr = string.Join(", ", promptTemplate.Stop.Select(s =>
                    string.Format(
                            CultureInfo.InvariantCulture,
                            """
                            "{0}"
                            """,
                            EscapeNewLines(s))));
                modelPromptTemplateSb.Append("    Stop = [ ");
                modelPromptTemplateSb.Append(stopStr);
                modelPromptTemplateSb.AppendLine("]");
            }

            modelPromptTemplateSb.Append(spaces);
            modelPromptTemplateSb.Append('}');

            return modelPromptTemplateSb.ToString();
        }

        private async Task<string> AddFilesFromSampleAsync(
            Sample sample,
            List<(string PackageName, string? Version)> packageReferences,
            string safeProjectName,
            string outputPath,
            bool addLllmTypes,
            Dictionary<ModelType, (string CachedModelDirectoryPath, string ModelUrl, bool IsSingleFile, string ModelPathStr, HardwareAccelerator HardwareAccelerator, PromptTemplate? ModelPromptTemplate)> modelInfos,
            CancellationToken cancellationToken)
        {
            var sharedCode = sample.SharedCode.ToList();
            if (!sharedCode.Contains(SharedCodeEnum.LlmPromptTemplate) &&
                (addLllmTypes || sample.SharedCode.Contains(SharedCodeEnum.GenAIModel)))
            {
                // Always used inside GenAIModel.cs
                sharedCode.Add(SharedCodeEnum.LlmPromptTemplate);
            }

            if (sharedCode.Contains(SharedCodeEnum.DeviceUtils) && !sharedCode.Contains(SharedCodeEnum.NativeMethods))
            {
                sharedCode.Add(SharedCodeEnum.NativeMethods);
                var csWin32 = "Microsoft.Windows.CsWin32";
                if (!packageReferences.Any(packageReferences => packageReferences.PackageName == csWin32))
                {
                    packageReferences.Add((csWin32, null));
                }
            }

            foreach (var sharedCodeEnum in sharedCode)
            {
                var fileName = SharedCodeHelpers.GetName(sharedCodeEnum);
                var source = SharedCodeHelpers.GetSource(sharedCodeEnum);
                if (fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    source = CleanXamlSource(source, $"{safeProjectName}.SharedCode", out _);
                }
                else
                {
                    source = CleanCsSource(source, $"{safeProjectName}.SharedCode", false);
                }

                await File.WriteAllTextAsync(Path.Join(outputPath, fileName), source, cancellationToken);
            }

            string className = "Sample";
            if (!string.IsNullOrEmpty(sample.XAMLCode))
            {
                var xamlSource = CleanXamlSource(sample.XAMLCode, safeProjectName, out className);
                xamlSource = xamlSource.Replace($"{Environment.NewLine}    xmlns:samples=\"using:AIDevGallery.Samples\"", string.Empty);
                xamlSource = xamlSource.Replace("<samples:BaseSamplePage", "<Page");
                xamlSource = xamlSource.Replace("</samples:BaseSamplePage>", "</Page>");

                await File.WriteAllTextAsync(Path.Join(outputPath, $"{className}.xaml"), xamlSource, cancellationToken);
            }

            if (!string.IsNullOrEmpty(sample.CSCode))
            {
                var cleanCsSource = CleanCsSource(sample.CSCode, safeProjectName, true);
                cleanCsSource = cleanCsSource.Replace("sampleParams.NotifyCompletion();", "App.Window?.ModelLoaded();");
                cleanCsSource = cleanCsSource.Replace(": BaseSamplePage", ": Microsoft.UI.Xaml.Controls.Page");
                cleanCsSource = cleanCsSource.Replace(
                    "Task LoadModelAsync(SampleNavigationParameters sampleParams)",
                    "void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)");
                cleanCsSource = cleanCsSource.Replace(
                    "Task LoadModelAsync(MultiModelSampleNavigationParameters sampleParams)",
                    "void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)");
                cleanCsSource = RegexReturnTaskCompletedTask().Replace(cleanCsSource, string.Empty);

                string modelPath;
                if (modelInfos.Count > 1)
                {
                    int i = 0;
                    foreach (var modelInfo in modelInfos)
                    {
                        cleanCsSource = cleanCsSource.Replace($"sampleParams.HardwareAccelerators[{i}]", $"HardwareAccelerator.{modelInfo.Value.HardwareAccelerator}");
                        cleanCsSource = cleanCsSource.Replace($"sampleParams.ModelPaths[{i}]", modelInfo.Value.ModelPathStr);
                        i++;
                    }

                    modelPath = modelInfos.First().Value.ModelPathStr;
                }
                else
                {
                    var modelInfo = modelInfos.Values.First();
                    cleanCsSource = cleanCsSource.Replace("sampleParams.HardwareAccelerator", $"HardwareAccelerator.{modelInfo.HardwareAccelerator}");
                    cleanCsSource = cleanCsSource.Replace("sampleParams.ModelPath", modelInfo.ModelPathStr);
                    modelPath = modelInfo.ModelPathStr;
                }

                cleanCsSource = cleanCsSource.Replace("sampleParams.CancellationToken", "CancellationToken.None");

                var search = "sampleParams.GetIChatClientAsync()";
                int index = cleanCsSource.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    int newLineIndex = cleanCsSource[..index].LastIndexOf(Environment.NewLine, StringComparison.OrdinalIgnoreCase);
                    var subStr = cleanCsSource[(newLineIndex + Environment.NewLine.Length)..];
                    var subStrWithoutSpaces = subStr.TrimStart();
                    var spaceCount = subStr.Length - subStrWithoutSpaces.Length;
                    var promptTemplate = GetPromptTemplateString(modelInfos.Values.First().ModelPromptTemplate, spaceCount);
                    var chatClientLoader = GetChatClientLoaderString(sample, modelPath, promptTemplate);
                    if (chatClientLoader != null)
                    {
                        cleanCsSource = cleanCsSource.Replace(search, chatClientLoader);
                    }
                }

                await File.WriteAllTextAsync(Path.Join(outputPath, $"{className}.xaml.cs"), cleanCsSource, cancellationToken);
            }

            return className;
        }

        [GeneratedRegex(@"x:Class=""(@?[a-z_A-Z]\w+(?:\.@?[a-z_A-Z]\w+)*)""")]
        private static partial Regex XClass();

        [GeneratedRegex(@"xmlns:local=""using:(\w.+)""")]
        private static partial Regex XamlLocalUsing();

        [GeneratedRegex(@"[\r\n][\s]*return Task.CompletedTask;")]
        private static partial Regex RegexReturnTaskCompletedTask();

        private string CleanXamlSource(string xamlCode, string newNamespace, out string className)
        {
            var match = XClass().Match(xamlCode);
            if (match.Success)
            {
                var oldClassFullName = match.Groups[1].Value;
                _ = oldClassFullName[..oldClassFullName.LastIndexOf('.')];
                className = oldClassFullName[(oldClassFullName.LastIndexOf('.') + 1)..];

                xamlCode = xamlCode.Replace(match.Value, @$"x:Class=""{newNamespace}.{className}""");
            }
            else
            {
                className = "Sample";
            }

            xamlCode = XamlLocalUsing().Replace(xamlCode, $"xmlns:local=\"using:{newNamespace}\"");

            xamlCode = xamlCode.Replace("xmlns:shared=\"using:AIDevGallery.Samples.SharedCode\"", $"xmlns:shared=\"using:{newNamespace}.SharedCode\"");

            return xamlCode;
        }

        [GeneratedRegex(@"using AIDevGallery\S*;\r?\n", RegexOptions.Multiline)]
        private static partial Regex UsingAIDevGalleryGNamespace();

        [GeneratedRegex(@"namespace AIDevGallery(?:[^;\r\n])*(;?)\r\n", RegexOptions.Multiline)]
        private static partial Regex AIDevGalleryGNamespace();

        private static string CleanCsSource(string source, string newNamespace, bool addSharedSourceNamespace)
        {
            // Remove the using statements for the AIDevGallery.* namespaces
            source = UsingAIDevGalleryGNamespace().Replace(source, string.Empty);

            source = source.Replace("\r\r", "\r");

            // Replace the AIDevGallery namespace with the namespace of the new project
            // consider the 1st capture group to add the ; or not
            var match = AIDevGalleryGNamespace().Match(source);
            if (match.Success)
            {
                source = AIDevGalleryGNamespace().Replace(source, $"namespace {newNamespace}{match.Groups[1].Value}{Environment.NewLine}");
            }

            if (addSharedSourceNamespace)
            {
                var namespaceLine = $"using {newNamespace}.SharedCode;";
                if (!source.Contains(namespaceLine))
                {
                    source = namespaceLine + Environment.NewLine + source;
                }
            }

            return source;
        }
    }
}