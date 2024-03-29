#region Licence...

/*
The MIT License (MIT)

Copyright (c) 2014 Oleg Shilo

Permission is hereby granted,
free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

#endregion Licence...

using Microsoft.Deployment.WindowsInstaller;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;
using WixSharp.Bootstrapper;
using IO = System.IO;

//WIX References:
//http://www.wixwiki.com/index.php?title=Main_Page

namespace WixSharp
{

    /// <summary>
    /// This class holds the settings for Wix# XML auto-generation: generation of WiX XML elements, which do not have direct
    /// representation in the Wix# script. The detailed information about Wix# auto-generation can be found here: http://www.csscript.net/WixSharp/ID_Allocation.html.
    /// </summary>
    public class AutoGenerationOptions
    {
        /// <summary>
        /// directories specified indirectly (e.g. by Shortcut working directory)
        /// </summary>
        //public bool GenerateImplicitDirs = true;
        //public bool GenerateSubDirsForComplexPaths = true; //Generate MyCompany and MyProduct directories for new Dir(%ProgramFiles%\My Company\My Product"...

        //it is important to have the ID as public (upper case). Otherwise WiX doesn't produce correct MSI. It is related
        //to Issue #35: Absolute path as INSTALLDIR doesn't work correctly with Files("*.*")
        public string InstallDirDefaultId = "INSTALLDIR";

        /// <summary>
        /// Flag indicating if all system folders (e.g. %ProgramFiles%) should be auto-mapped into their x64 equivalents
        /// when 'project.Platform = Platform.x64'
        /// </summary>
        public bool Map64InstallDirs = true;
    }

    /// <summary>
    /// Delegate for  <see cref="Compiler"/> event <c>WixSourceGenerated</c>
    /// </summary>
    public delegate void XDocumentGeneratedDlgt(XDocument document);

    /// <summary>
    /// Delegate for  <see cref="Compiler"/> event <c>WixSourceSaved</c>
    /// </summary>
    public delegate void XDocumentSavedDlgt(string fileName);

    /// <summary>
    /// Delegate for  <see cref="Compiler"/> event <c>WixSourceFormated</c>
    /// </summary>
    public delegate void XDocumentFormatedDlgt(ref string content);

    /// <summary>
    /// Represents Wix# compiler. This class is responsible for conversion of CLR object <see cref="Project"/> into WiX XML source file.
    /// <see cref="Compiler"/> allows building complete MSI or WiX source file. It also can prepare WiX source file and generate corresponding batch file
    /// for building MSI WiX way: <c>candle.exe</c> + <c>light.exe</c>.
    /// <para>
    /// This class contains only static members as it is to be used only for the actual MSI/WXS building operations:
    /// </para>
    /// </summary>
    ///
    /// <example>
    ///
    /// <list type="bullet">
    ///
    /// <item>
    /// <description>Building MSI file
    /// <code>
    /// var project = new Project();
    /// ...
    /// Compiler.BuildMsi(project);
    /// </code>
    /// </description>
    /// </item>
    ///
    /// <item>
    /// <description>Building WiX source file only:
    /// <code>
    /// var project = new Project();
    /// ...
    /// Compiler.BuildWxs(project);
    /// </code>
    /// </description>
    /// </item>
    ///
    /// <item>
    /// <description>Preparing batch file for building MSI with WiX toolset:
    /// <code>
    /// var project = new Project();
    /// ...
    /// Compiler.BuildWxsCmd(project);
    /// </code>
    /// </description>
    /// </item>
    ///
    /// </list>
    ///
    /// </example>
    public partial class Compiler
    {
        /// <summary>
        /// Contains settings for XML auto-generation.
        /// </summary>
        static public AutoGenerationOptions AutoGeneration = new AutoGenerationOptions();

        /// <summary>
        /// Occurs when WiX source code generated. Use this event if you need to modify generated XML (XDocument)
        /// before it is compiled into MSI.
        /// </summary>
        static public event XDocumentGeneratedDlgt WixSourceGenerated;

        /// <summary>
        /// Occurs when WiX source file is saved. Use this event if you need to do any post-processing of the generated/saved file.
        /// </summary>
        static public event XDocumentSavedDlgt WixSourceSaved;

        /// <summary>
        /// Occurs when WiX source file is formatted and ready to be saved. Use this event if you need to do any custom formatting of the XML content before
        /// it is saved by the compiler.
        /// </summary>
        static public event XDocumentFormatedDlgt WixSourceFormated;

        /// <summary>
        /// WiX linker <c>Light.exe</c> options.
        /// <para>The default value is "-sw1076 -sw1079" (disable warning 1076 and 1079).</para>
        /// </summary>
        static public string LightOptions = "-sw1076 -sw1079";

        /// <summary>
        /// WiX compiler <c>Candle.exe</c> options.
        /// <para>The default value is "-sw1076" (disable warning 1026).</para>
        /// </summary>
        static public string CandleOptions = "-sw1026";

        static string autogeneratedWxsForVS = null;

        static Compiler()
        {
            //Debug.Assert(false);
            EnsureVSIntegration();
        }

        static void EnsureVSIntegration()
        {
            try
            {
                string lastArg = Environment.GetCommandLineArgs().LastOrDefault() ?? "";

                // /MBSBUILD:$(ProjectName)
                var preffix = "/MBSBUILD:";
                if (lastArg.StartsWith(preffix))
                {
                    //if building as part of the VS project with WixSharp NuGet package create (auto-generated) wxs file

                    string projName = lastArg.Substring(preffix.Length);

                    //MSBuild always sets currdir to the project directory
                    string destDir = IO.Path.Combine(Environment.CurrentDirectory, "wix");
                    string projFile = IO.Path.Combine(Environment.CurrentDirectory, projName + ".csproj");

                    if (!IO.Directory.Exists(destDir))
                        IO.Directory.CreateDirectory(destDir);

                    autogeneratedWxsForVS = IO.Path.Combine(destDir, projName + ".g.wxs");

                    string autogenItem = "wix\\$(ProjectName).g.wxs";

                    var doc = XDocument.Load(projFile);
                    var ns = doc.Root.Name.Namespace;

                    bool injected = doc.Root.Descendants(ns + "None").Where(e => e.Attributes("Include").Where(a => a.Value == autogenItem).Any()).Any();
                    if (!injected)
                    {
                        var itemGroup = doc.Root.Descendants().Where(e => e.Name.LocalName == "Compile").First().Parent;
                        itemGroup.Add(new XElement(ns + "None", new XAttribute("Include", autogenItem)));
                        doc.Save(projFile);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets or sets the location of WiX binaries (compiler/linker/dlls).
        /// The default value is the content of environment variable <c>WIXSHARP_WIXDIR</c>.
        /// <para>If user does not set this property explicitly and WIXSHARP_WIXDIR is not defined
        /// <see cref="Compiler"/> will try to locate WiX binaries in <c>Program Files\Windows Installer XML v&lt;max_of 3*&gt;\bin</c>
        /// </para>
        /// </summary>
        /// <value>The WiX binaries' location.</value>
        static public string WixLocation
        {
            get
            {
                if (wixLocation.IsEmpty()) //WixSharp did not set WIXSHARP_WIXDIR environment variable so check if the full WiX was installed
                {
                    var dir = Environment.ExpandEnvironmentVariables("%WIX%\\bin");

                    if (!IO.Directory.Exists(dir))
                    {
                        string wixDir = IO.Directory.GetDirectories(Utils.ProgramFilesDirectory, "Windows Installer XML v3*")
                                                    .OrderBy(x => x)
                                                    .LastOrDefault();

                        if (wixDir.IsEmpty())
                            wixDir = IO.Directory.GetDirectories(Utils.ProgramFilesDirectory, "WiX Toolset v3*")
                                                 .OrderBy(x => x)
                                                 .LastOrDefault();

                        if (!wixDir.IsEmpty())
                            dir = Utils.PathCombine(wixDir, "bin");
                    }

                    if (!IO.Directory.Exists(dir))
                        throw new Exception("WiX binaries cannot be found. Please set environment variable WIXSHARP_WIXDIR or WixSharp.Compiler.WixLocation to valid path to the Wix binaries.");

                    wixLocation = dir;
                }

                return wixLocation;
            }
            set { wixLocation = value; }
        }

        static string wixLocation = Environment.GetEnvironmentVariable("WIXSHARP_WIXDIR");

        static string wixSdkLocation;

        /// <summary>
        /// Gets or sets the location of WiX SDK binaries (e.g. MakeSfxCA.exe).
        /// The default value is the 'SDK' sub-directory of WixSharp.Compiler.WixLocation directory.
        /// <para>
        /// If for whatever reason the default location is invalid, you can always set this property to the location of your choice.
        /// </para>
        /// </summary>
        /// <value>
        /// The WiX SDK location.
        /// </value>
        /// <exception cref="System.Exception">WiX SDK binaries cannot be found. Please set WixSharp.Compiler.WixSdkLocation to valid path to the Wix SDK binaries.</exception>
        public static string WixSdkLocation
        {
            get
            {
                if (wixSdkLocation.IsEmpty())
                    wixSdkLocation = IO.Path.GetFullPath(Utils.PathCombine(WixLocation, "..\\sdk"));

                if (!IO.Directory.Exists(wixSdkLocation))
                    throw new Exception("WiX SDK binaries cannot be found. Please set WixSharp.Compiler.WixSdkLocation to valid path to the Wix SDK binaries.");

                return wixSdkLocation;
            }
            set
            {
                wixSdkLocation = value;
            }
        }

        /// <summary>
        /// Forces <see cref="Compiler"/> to preserve all temporary build files (e.g. *.wxs).
        /// <para>The default value is <c>false</c>: all temporary files are deleted at the end of the build/compilation.</para>
        /// <para>Note: if <see cref="Compiler"/> fails to build MSI the <c>PreserveTempFiles</c>
        /// value is ignored and all temporary files are preserved.</para>
        /// </summary>
        static public bool PreserveTempFiles = false;

        /// <summary>
        /// Indicates whether compiler should emit relative or absolute paths in the WiX XML source.
        /// </summary>
        static public bool EmitRelativePaths = true;

        /// <summary>
        /// Gets or sets the GUID generator algorithm. You can use either one of the built-in algorithms or define your own.
        /// The default value is <see cref="GuidGenerators.Default"/>.
        /// <description>Possible WiX source file only:
        /// <code>
        /// //default built-in seeded GUID generator
        /// Compiler.GuidGenerator = GuidGenerators.Default;
        ///
        /// //sequential built-in GUID generator
        /// Compiler.GuidGenerator = GuidGenerators.Sequential;
        ///
        /// //Custom 'aways-same' GUID generator
        /// Compiler.GuidGenerator = (seed) => Guid.Parse("9e2974a1-9539-4c5c-bef7-80fc35b9d7b0");
        ///
        /// //Custom random GUID generator
        /// Compiler.GuidGenerator = (seed) => Guid.NewGuid();
        /// </code>
        /// </description>
        /// </summary>
        /// <value>
        /// The GUID generator algorithm.
        /// </value>
        public static Func<object, Guid> GuidGenerator
        {
            get { return WixGuid.Generator; }
            set { WixGuid.Generator = value; }
        }

        public static List<string> TempFiles = new List<string>();

        /// <summary>
        /// Builds the MSI file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Path to the built MSI file. Returns <c>null</c> if <c>MSI</c> cannot be built.</returns>
        static public string BuildMsi(Project project)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            return Build(project, OutputType.MSI);
        }

        static string Build(Project project, OutputType type)
        {
            string outFile = IO.Path.GetFullPath(IO.Path.Combine(project.OutDir, project.OutFileName) + "." + type.ToString().ToLower());

            Utils.EnsureFileDir(outFile);

            if (IO.File.Exists(outFile))
                IO.File.Delete(outFile);

            Build(project, outFile, type);

            return IO.File.Exists(outFile) ? outFile : null;
        }

        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSI with WiX toolset.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Path to the batch file.</returns>
        static public string BuildMsiCmd(Project project)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            string cmdFile = IO.Path.GetFullPath(IO.Path.Combine(project.OutDir, "Build_" + project.OutFileName) + ".cmd");

            if (IO.File.Exists(cmdFile))
                IO.File.Delete(cmdFile);
            BuildMsiCmd(project, cmdFile);
            return cmdFile;
        }

        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSM with WiX toolset.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Path to the batch file.</returns>
        static public string BuildMsmCmd(Project project)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            string cmdFile = IO.Path.GetFullPath(IO.Path.Combine(project.OutDir, "Build_" + project.OutFileName) + ".cmd");

            if (IO.File.Exists(cmdFile))
                IO.File.Delete(cmdFile);
            BuildMsmCmd(project, cmdFile);
            return cmdFile;
        }

        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSI with WiX toolset.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="path">The path to the batch file to be build.</param>
        /// <returns>Path to the batch file.</returns>
        static public string BuildMsiCmd(Project project, string path)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            BuildCmd(project, path, OutputType.MSI);
            return path;
        }


        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSM with WiX toolset.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="path">The path to the batch file to be build.</param>
        /// <returns>Path to the batch file.</returns>
        static public string BuildMsmCmd(Project project, string path)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            BuildCmd(project, path, OutputType.MSM);
            return path;
        }

        static void BuildCmd(Project project, string path, OutputType type)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            string compiler = Utils.PathCombine(WixLocation, "candle.exe");
            string linker = Utils.PathCombine(WixLocation, "light.exe");
            string batchFile = path;

            if (!IO.File.Exists(compiler) && !IO.File.Exists(linker))
            {
                Console.WriteLine("Wix binaries cannot be found. Expected location is " + IO.Path.GetDirectoryName(compiler));
                throw new ApplicationException("Wix compiler/linker cannot be found");
            }
            else
            {
                string wxsFile = BuildWxs(project, type);
                string objFile = IO.Path.GetFileNameWithoutExtension(wxsFile) + ".wixobj";

                string extensionDlls = "";
                foreach (string dll in project.WixExtensions.Distinct())
                    extensionDlls += " -ext \"" + ResolveExtensionFile(dll) + "\"";

                var candleOptions = CandleOptions + " " + project.CandleOptions;
                var lightOptions = LightOptions + " " + project.LightOptions;

                string batchFileContent = "\"" + compiler + "\" " + candleOptions + " " + extensionDlls + " \"" + IO.Path.GetFileName(wxsFile) + "\"\r\n";

                if (project.IsLocalized && IO.File.Exists(project.LocalizationFile))
                    batchFileContent += "\"" + linker + "\" " + lightOptions + " \"" + objFile + "\" " + extensionDlls + " -cultures:" + project.Language + " -loc " + project.LocalizationFile + "\r\npause";
                else
                    batchFileContent += "\"" + linker + "\" " + lightOptions + " \"" + objFile + "\" " + extensionDlls + " -cultures:" + project.Language + "\r\npause";

                using (var sw = new IO.StreamWriter(batchFile))
                    sw.Write(batchFileContent);
            }
        }

        /// <summary>
        /// Builds the MSI file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="path">The path to the MSI file to be build.</param>
        /// <returns>Path to the built MSI file.</returns>
        static public string BuildMsi(Project project, string path)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            Build(project, path, OutputType.MSI);
            return path;
        }

        /// <summary>
        /// Builds the MSM file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Path to the built MSM file. Returns <c>null</c> if <c>msm</c> cannot be built.</returns>
        static public string BuildMsm(Project project)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;
            return Build(project, OutputType.MSM);
        }

        /// <summary>
        /// Builds the MSM file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="path">The path to the MSM file to be build.</param>
        /// <returns>Path to the built MSM file.</returns>
        static public string BuildMsm(Project project, string path)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            Build(project, path, OutputType.MSM);

            return path;
        }

        /// <summary>
        /// Specifies the type of the setup binaries to build.
        /// </summary>
        public enum OutputType
        {
            /// <summary>
            /// MSI file.
            /// </summary>
            MSI,

            /// <summary>
            /// Merge Module (MSM) file.
            /// </summary>
            MSM
        }

        static void CopyAsAutogen(string source, string dest)
        {
            // Debug.Assert(false);
            string[] header = @"<!--
<auto-generated>
    This code was generated by WixSharp.
    Changes to this file will be lost if the code is regenerated.
</auto-generated>
-->".Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            var content = new List<string>(IO.File.ReadAllLines(source));
            content.InsertRange(1, header); //first line must be an XML declaration

            if (IO.File.Exists(dest))
                IO.File.SetAttributes(dest, IO.FileAttributes.Normal); //just in case if it was set read-only

            IO.File.WriteAllLines(dest, content.ToArray());
        }

        static void Build(Project project, string path, OutputType type)
        {
            string oldCurrDir = Environment.CurrentDirectory;

            try
            {
                //System.Diagnostics.Debug.Assert(false);
                Compiler.TempFiles.Clear();
                string compiler = Utils.PathCombine(WixLocation, @"candle.exe");
                string linker = Utils.PathCombine(WixLocation, @"light.exe");

                if (!IO.File.Exists(compiler) || !IO.File.Exists(linker))
                {
                    Console.WriteLine("Wix binaries cannot be found. Expected location is " + IO.Path.GetDirectoryName(compiler));
                    throw new ApplicationException("Wix compiler/linker cannot be found");
                }
                else
                {
                    string wxsFile = BuildWxs(project, type);

                    if (autogeneratedWxsForVS != null)
                        CopyAsAutogen(wxsFile, autogeneratedWxsForVS);

                    if (!project.SourceBaseDir.IsEmpty())
                        Environment.CurrentDirectory = project.SourceBaseDir;

                    string objFile = IO.Path.ChangeExtension(wxsFile, ".wixobj");
                    string pdbFile = IO.Path.ChangeExtension(wxsFile, ".wixpdb");

                    string extensionDlls = "";
                    foreach (string dll in project.WixExtensions.Distinct())
                        extensionDlls += " -ext \"" + dll + "\"";

                    var candleOptions = CandleOptions + " " + project.CandleOptions;
                    var lightOptions = LightOptions + " " + project.LightOptions;

                    //AppDomain.CurrentDomain.ExecuteAssembly(compiler, null, new string[] { projFile }); //a bit unsafer version
                    Run(compiler, candleOptions + " " + extensionDlls + " \"" + wxsFile + "\" -out \"" + objFile + "\"");

                    if (IO.File.Exists(objFile))
                    {
                        string msiFile = IO.Path.ChangeExtension(wxsFile, "." + type.ToString().ToLower());
                        if (IO.File.Exists(msiFile))
                            IO.File.Delete(msiFile);

                        if (project.IsLocalized && IO.File.Exists(project.LocalizationFile))
                            Run(linker, lightOptions + " \"" + objFile + "\" -out \"" + msiFile + "\"" + extensionDlls + " -cultures:" + project.Language + " -loc \"" + project.LocalizationFile + "\"");
                        else
                            Run(linker, lightOptions + " \"" + objFile + "\" -out \"" + msiFile + "\"" + extensionDlls + " -cultures:" + project.Language);

                        if (IO.File.Exists(msiFile))
                        {
                            Compiler.TempFiles.Add(wxsFile);

                            Console.WriteLine("\n----------------------------------------------------------\n");
                            Console.WriteLine(type + " file has been built: " + path + "\n");
                            Console.WriteLine((type == OutputType.MSI ? " ProductName: " : " ModuleName: ") + project.Name);
                            Console.WriteLine(" Version    : " + project.Version);
                            Console.WriteLine(" ProductId  : {" + project.ProductId + "}");
                            Console.WriteLine(" UpgradeCode: {" + project.UpgradeCode + "}\n");
                            if (!project.AutoAssignedInstallDirPath.IsEmpty())
                            {
                                Console.WriteLine(" Auto-generated InstallDir ID:");
                                Console.WriteLine("   " + Compiler.AutoGeneration.InstallDirDefaultId + "=" + project.AutoAssignedInstallDirPath);
                            }
                            IO.File.Delete(objFile);
                            IO.File.Delete(pdbFile);

                            if (path != msiFile)
                            {
                                if (IO.File.Exists(path))
                                    IO.File.Delete(path);
                                IO.File.Move(msiFile, path);
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Cannot build " + wxsFile);
                        Trace.WriteLine("Cannot build " + wxsFile);
                    }
                }

                if (!PreserveTempFiles && !project.PreserveTempFiles)
                    foreach (var file in Compiler.TempFiles)
                        try
                        {
                            if (IO.File.Exists(file))
                                IO.File.Delete(file);
                        }
                        catch { }
            }
            finally
            {
                Environment.CurrentDirectory = oldCurrDir;
            }
        }

        /// <summary>
        /// Builds the WiX source file (*.wxs) from the specified <see cref="Project"/> instance for further compiling into MSI file.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Path to the built WXS file.</returns>
        public static string BuildWxs(Project project)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            return BuildWxs(project, OutputType.MSI);
        }

        /// <summary>
        /// Builds the WiX source file (*.wxs) from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="type">The type (<see cref="OutputType"/>) of the setup file to be defined in the source file (MSI vs. MSM).</param>
        /// <returns>Path to the built WXS file.</returns>
        public static string BuildWxs(Project project, OutputType type)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            WixEntity.ResetIdGenerator();
            string file = IO.Path.GetFullPath(IO.Path.Combine(project.OutDir, project.OutFileName) + ".wxs");

            if (IO.File.Exists(file))
                IO.File.Delete(file);

            BuildWxs(project, file, type);
            return file;
        }

        /// <summary>
        /// Builds the WiX source file (*.wxs) from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <param name="path">The path to the WXS file to be build.</param>
        /// <param name="type">The type (<see cref="OutputType"/>) of the setup file to be defined in the source file (MSI vs. MSM).</param>
        /// <returns>Path to the built WXS file.</returns>
        public static string BuildWxs(Project project, string path, OutputType type)
        {
            //very important to keep "ClientAssembly = " in all "public Build*" methods to ensure GetCallingAssembly
            //returns the build script assembly but not just another method of Compiler.
            if (ClientAssembly.IsEmpty())
                ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            XDocument doc = GenerateWixProj(project);

            IndjectCustomUI(project.CustomUI, doc);
            DefaultWixSourceGeneratedHandler(doc);
            AutoElements.InjectAutoElementsHandler(doc);
            AutoElements.NormalizeFilePaths(doc, project.SourceBaseDir, EmitRelativePaths);

            if (type == OutputType.MSM)
            {
                //remove all pure MSI elements
                ConvertMsiToMsm(doc);
            }

            project.InvokeWixSourceGenerated(doc);
            if (WixSourceGenerated != null)
                WixSourceGenerated(doc);

            string xml = "";
            using (IO.StringWriter sw = new StringWriterWithEncoding(project.Encoding))
            {
                doc.Save(sw, SaveOptions.None);
                xml = sw.ToString();
            }

            //of course you can use XmlTextWriter.WriteRaw but this is just a temporary quick'n'dirty solution
            //http://forums.microsoft.com/MSDN/ShowPost.aspx?PostID=2657663&SiteID=1
            xml = xml.Replace("xmlns=\"\"", "");

            DefaultWixSourceFormatedHandler(ref xml);

            project.InvokeWixSourceFormated(ref xml);
            if (WixSourceFormated != null)
                WixSourceFormated(ref xml);

            using (var sw = new IO.StreamWriter(path, false, project.Encoding))
                sw.WriteLine(xml);

            Console.WriteLine("\n----------------------------------------------------------\n");
            Console.WriteLine("Wix project file has been built: " + path + "\n");

            project.InvokeWixSourceSaved(path);
            if (WixSourceSaved != null)
                WixSourceSaved(path);

            return path;
        }

        //This class is needed to overcome the StringWriter limitation when encoding cannot be changed
        internal class StringWriterWithEncoding : IO.StringWriter
        {
            public StringWriterWithEncoding(Encoding encoding)
            {
                this.encoding = encoding;
            }

            Encoding encoding = Encoding.Default;

            public override Encoding Encoding { get { return encoding; } }
        }

        static void IndjectCustomUI(CustomUI customUI, XDocument doc)
        {
            if (customUI != null)
                doc.Root.Select("Product").Add(customUI.ToXElement());
        }

        /// <summary>
        /// The default <see cref="Compiler.WixSourceGenerated"/> event handler.
        /// </summary>
        /// <param name="doc">The XDocument object representing WiX XML source code.</param>
        public static void DefaultWixSourceGeneratedHandler(XDocument doc)
        {
            Func<string, XElement[]> extract =
                name => doc.Root.Select("Product")
                                .Elements()
                                .Where(e =>
                                    {
                                        if (name.StartsWith("*"))
                                            return e.Name.LocalName.EndsWith(name.Substring(1));
                                        else
                                            return e.Name == name;
                                    })
                                .ToArray();

            //order references to Product.Elements()
            var orderedElements = extract("CustomAction")
                                 .Concat(extract("Binary"))
                                 .Concat(extract("UIRef"))
                                 .Concat(extract("Feature"))
                                 .Concat(extract("*Sequence"));

            //move elements to be ordered to the end of the doc
            foreach (var e in orderedElements)
            {
                e.Remove();
                doc.Root.Select("Product").Add(e);
            }
        }

        static void ConvertMsiToMsm(XDocument doc)
        {
            Func<string, XElement[]> extract =
               name => (from e in doc.Root.Select("Product").Elements()
                        where e.Name.LocalName == name
                        select e).ToArray();

            var elementsToRemove = extract("Feature")
                                   .Concat(
                                   extract("Media"));

            //var elementsToRemove1 = extract("Media");
            foreach (var e in elementsToRemove)
                e.Remove();

            XElement product = doc.Root.Select("Product");
            product.Remove();

            XElement module = doc.Root.AddElement(new XElement("Module", product.Elements()));
            module.CopyAttributeFrom("Id", product, "Name")
                  .CopyAttributeFrom(product, "Codepage")
                  .CopyAttributeFrom(product, "Language")
                  .CopyAttributeFrom(product, "Version");

            XElement package = module.Select("Package");
            package.CopyAttributeFrom(product, "Id")
                   .CopyAttributeFrom(product, "Manufacturer")
                   .Attribute("Compressed").Remove();
        }

        /// <summary>
        /// The default <see cref="Compiler.WixSourceFormated"/> event handler.
        /// </summary>
        /// <param name="xml">The XML text string representing WiX XML source code.</param>
        public static void DefaultWixSourceFormatedHandler(ref string xml)
        {
            //very superficial formatting

            var mergeSections = new[] { "<Wix ", "<Media ", "<File ", "<MultiStringValue>" };
            var splitSections = new[] { "</Product>", "</Module>" };

            StringBuilder sb = new StringBuilder();
            using (var sr = new IO.StringReader(xml))
            using (var sw = new IO.StringWriter(sb))
            {
                string line = "";
                string prevLine = "";
                while ((line = sr.ReadLine()) != null)
                {
                    if (prevLine.Trim().IsEmpty() && line.Trim().IsEmpty())
                        continue;

                    string lineTrimmed = line.Trim();
                    string prevLineTrimmed = prevLine.Trim();

                    if (!prevLine.Trim().IsEmpty() && prevLine.GetLeftIndent() == line.GetLeftIndent())
                    {
                        var delimiters = " ".ToCharArray();
                        var prevLineTokens = prevLine.Trim().Split(delimiters, 2);
                        var lineTokens = lineTrimmed.Split(delimiters, 2);
                        if (prevLineTokens.First() != lineTokens.First())
                        {
                            bool preventSpliting = false;
                            foreach (var token in mergeSections)
                                if (preventSpliting = lineTrimmed.StartsWith(token))
                                    break;

                            if (!preventSpliting)
                                sw.WriteLine();
                        }
                    }
                    else
                    {
                        if (lineTrimmed.StartsWith("<Component ")) //start of another component
                            sw.WriteLine();
                        else if (lineTrimmed == "</Directory>" && prevLineTrimmed == "</Component>") //last component
                            sw.WriteLine();
                    }

                    foreach (var token in splitSections)
                        if (line.Trim().StartsWith(token))
                            sw.WriteLine();

                    sw.WriteLine(line);
                    prevLine = line;
                }
            }

            xml = sb.ToString();
        }

        static void ResetCachedContent()
        {
            autogeneratedShortcutLocations.Clear();
        }

        /// <summary>
        /// Generates WiX XML source file the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="project">The <see cref="Project"/> instance.</param>
        /// <returns>Instance of XDocument class representing in-memory WiX XML source file.</returns>
        public static XDocument GenerateWixProj(Project project)
        {
            project.Preprocess();

            ProjectValidator.Validate(project);

            project.ControlPanelInfo.AddMembersTo(project);

            project.AutoAssignedInstallDirPath = "";

            project.GenerateProductGuids();
            ResetCachedContent();

            string extraNamespaces = project.WixNamespaces.Distinct()
                                                          .Select(x => x.StartsWith("xmlns:") ? x : "xmlns:" + x)
                                                          .ConcatItems(" ");

            XDocument doc = XDocument.Parse(
                    @"<?xml version=""1.0"" encoding=""utf-8""?>
                         <Wix xmlns=""http://schemas.microsoft.com/wix/2006/wi"" " + extraNamespaces + @">
                            <Product>
                                <Package InstallerVersion=""200"" Compressed=""yes""/>
                                <Media Id=""1"" Cabinet=""" + (project as WixEntity).Id + @".cab"" EmbedCab=""yes"" />
                            </Product>
                        </Wix>");

            XElement product = doc.Root.Select("Product");
            product.Add(new XAttribute("Id", project.ProductId),
                         new XAttribute("Name", project.Name),
                         new XAttribute("Language", new CultureInfo(project.Language).LCID),
                         new XAttribute("Codepage", project.Codepage),
                         new XAttribute("Version", project.Version),
                         new XAttribute("UpgradeCode", project.UpgradeCode));

            if (project.ControlPanelInfo != null && project.ControlPanelInfo.Manufacturer.IsNotEmpty())
                product.SetAttribute("Manufacturer", project.ControlPanelInfo.Manufacturer);
            product.AddAttributes(project.Attributes);

            XElement package = product.Select("Package");

            package.SetAttribute("Description", project.Description)
                   .SetAttribute("Platform", project.Platform)
                   .SetAttribute("SummaryCodepage", project.Codepage)
                   .SetAttribute("Languages", new CultureInfo(project.Language).LCID)
                   .SetAttribute("InstallScope", project.InstallScope);

            if (project.EmitConsistentPackageId)
                package.CopyAttributeFrom(product, "Id");

            package.AddAttributes(project.Package.Attributes);

            product.Select("Media").AddAttributes(project.Media.Attributes);

            ProcessLaunchConditions(project, product);

            //extend wDir
            XElement dirs = product.AddElement(
                        new XElement("Directory",
                            new XAttribute("Id", "TARGETDIR"),
                            new XAttribute("Name", "SourceDir")));

            var featureComponents = new Dictionary<Feature, List<string>>(); //feature vs. component IDs
            var autoGeneratedComponents = new List<string>(); //component IDs
            var defaultFeatureComponents = new List<string>(); //default Feature (Complete setup) components

            ProcessDirectories(project, featureComponents, defaultFeatureComponents, autoGeneratedComponents, dirs);
            ProcessRegKeys(project, featureComponents, defaultFeatureComponents, product);
            ProcessEnvVars(project, featureComponents, defaultFeatureComponents, product);
            ProcessUsers(project, featureComponents, defaultFeatureComponents, product);
            ProcessSql(project, featureComponents, defaultFeatureComponents, product);
            ProcessCertificates(project, featureComponents, defaultFeatureComponents, product);
            ProcessProperties(project, product);
            ProcessCustomActions(project, product);
            ProcessBinaries(project, product); //it is important to call ProcessBinaries after all other ProcessX as they may insert some implicit "binaries"
            ProcessFeatures(project, product, featureComponents, autoGeneratedComponents, defaultFeatureComponents);

            //special properties
            if (project.UI != WUI.WixUI_ProgressOnly)
            {
                XElement installDir = GetTopLevelDir(product);

                //if UIRef is set to WIXUI_INSTALLDIR must also add
                //<Property Id="WIXUI_INSTALLDIR" Value="directory id" />
                if (project.UI == WUI.WixUI_InstallDir)
                    product.Add(new XElement("Property",
                                    new XAttribute("Id", "WIXUI_INSTALLDIR"),
                                    new XAttribute("Value", installDir.Attribute("Id").Value)));

                product.Add(new XElement("UIRef",
                                new XAttribute("Id", project.UI.ToString())));

                var extensionAssembly = Utils.PathCombine(WixLocation, @"WixUIExtension.dll");
                if (project.WixExtensions.Find(x => x == extensionAssembly) == null)
                    project.WixExtensions.Add(extensionAssembly);
            }

            if (project.EmbeddedUI != null)
            {
                string bynaryPath = project.EmbeddedUI.Name;
                if (project.EmbeddedUI is EmbeddedAssembly)
                {
                    var asmBin = project.EmbeddedUI as EmbeddedAssembly;

                    bynaryPath = asmBin.Name.PathChangeDirectory(project.OutDir.PathGetFullPath())
                                            .PathChangeExtension(".CA.dll");

                    var refAsms = asmBin.RefAssemblies.Add(typeof(Session).Assembly.Location)
                                                      .Concat(project.DefaultRefAssemblies)
                                                      .Distinct()
                                                      .ToArray();

                    PackageManagedAsm(asmBin.Name, bynaryPath, refAsms, project.OutDir, project.CustomActionConfig);
                }

                product.AddElement("UI")
                       .Add(new XElement("EmbeddedUI",
                                new XAttribute("Id", project.EmbeddedUI.Id),
                                new XAttribute("SourceFile", bynaryPath)));

                product.Select("UIRef").Remove();
            }

            if (!project.BannerImage.IsEmpty())
            {
                product.Add(new XElement("WixVariable",
                    new XAttribute("Id", "WixUIBannerBmp"),
                    new XAttribute("Value", Utils.PathCombine(project.SourceBaseDir, project.BannerImage))));
            }

            if (!project.BackgroundImage.IsEmpty())
            {
                product.Add(new XElement("WixVariable",
                    new XAttribute("Id", "WixUIDialogBmp"),
                    new XAttribute("Value", Utils.PathCombine(project.SourceBaseDir, project.BackgroundImage))));
            }

            if (!project.LicenceFile.IsEmpty())
            {
                if (!AllowNonRtfLicense && !project.LicenceFile.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                    throw new ApplicationException("License file must have 'rtf' file extension. Specify 'Compiler.AllowNonRtfLicense=true' to overcome this constrain.");

                product.Add(
                       new XElement("WixVariable",
                       new XAttribute("Id", "WixUILicenseRtf"),
                       new XAttribute("Value", Utils.PathCombine(project.SourceBaseDir, project.LicenceFile)),
                       new XAttribute("xmlns", "")));
            }

            PostProcessMsm(project, product);
            ProcessUpgradeStrategy(project, product);

            return doc;
        }

        /// <summary>
        /// Defines if license file can be have non RTF extension.
        /// </summary>
        static public bool AllowNonRtfLicense = false;

        static XElement GetTopLevelDir(XElement product)
        {
            XElement dir = product.Elements("Directory").First();
            XElement prevDir = null;
            while (dir != null)
            {
                prevDir = dir;
                if (dir.Elements("Component").Count() == 0) //just a subdirectory without any installable items
                    dir = dir.Elements("Directory").First();
                else
                    return dir; //dir containing installable items (e.g. files or shortcuts)
            }

            return prevDir;
        }

        static void ProcessFeatures(Project project, XElement product, Dictionary<Feature, List<string>> featureComponents, List<string> autoGeneratedComponents, List<string> defaultFeatureComponents)
        {
            if (!featureComponents.ContainsKey(project.DefaultFeature))
            {
                featureComponents[project.DefaultFeature] = new List<string>(defaultFeatureComponents); //assign defaultFeatureComponents to the project's default Feature
            }
            else
            {
                foreach (string comp in defaultFeatureComponents)
                    featureComponents[project.DefaultFeature].Add(comp);
            }

            var feature2XML = new Dictionary<Feature, XElement>();

            //generate disconnected XML nodes
            foreach (Feature wFeature in featureComponents.Keys)
            {
                var comps = featureComponents[wFeature];

                XElement xFeature = new XElement("Feature",
                                                  new XAttribute("Id", wFeature.Id),
                                                  new XAttribute("Title", wFeature.Name),
                                                  new XAttribute("Absent", wFeature.AllowChange ? "allow" : "disallow"),
                                                  new XAttribute("Level", wFeature.IsEnabled ? "1" : "2"))
                                                  .AddAttributes(wFeature.Attributes);

                if (!wFeature.Description.IsEmpty())
                    xFeature.SetAttributeValue("Description", wFeature.Description);

                if (!wFeature.ConfigurableDir.IsEmpty())
                    xFeature.SetAttributeValue("ConfigurableDirectory", wFeature.ConfigurableDir);

                if (wFeature.Condition != null)
                    //intentionally leaving out AddAttributes(...) as Level is the only valid attribute on */Feature/Condition
                    xFeature.Add(
                        new XElement("Condition",
                            new XAttribute("Level", wFeature.Condition.Level),
                            new XCData(wFeature.Condition.ToCData())));

                foreach (string componentId in featureComponents[wFeature])
                    xFeature.Add(new XElement("ComponentRef",
                                    new XAttribute("Id", componentId)));

                foreach (string componentId in autoGeneratedComponents)
                    xFeature.Add(new XElement("ComponentRef",
                                    new XAttribute("Id", componentId)));

                feature2XML.Add(wFeature, xFeature);
            }

            //establish relationships
            foreach (Feature wFeature in featureComponents.Keys)
            {
                if (wFeature.Children.Count != 0)
                {
                    foreach (Feature wChild in wFeature.Children)
                    {
                        wChild.Parent = wFeature;
                        XElement xFeature = feature2XML[wFeature];

                        if (feature2XML.ContainsKey(wChild))
                        {
                            XElement xChild = feature2XML[wChild];
                            xFeature.Add(xChild);
                        }
                    }
                }
            }

            //remove childless features as they have non practical value
            feature2XML.Keys
                       .Where(x => !feature2XML[x].HasElements)
                       .ToArray()
                       .ForEach(key => feature2XML.Remove(key));

            var topLevelFeatures = feature2XML.Keys
                                              .Where(x=>x.Parent == null)
                                              .Select(x=>feature2XML[x]);

            foreach (XElement xFeature in topLevelFeatures)
            {
                product.AddElement(xFeature);
            }
        }

        static void ProcessUpgradeStrategy(Project project, XElement product)
        {
            if (project.MajorUpgradeStrategy != null)
            {
                Func<string, string> ExpandVersion = (version) => version == "%this%" ? project.Version.ToString() : version;

                var upgradeElement = product.AddElement(
                    new XElement("Upgrade",
                       new XAttribute("Id", project.UpgradeCode)));

                if (project.MajorUpgradeStrategy.UpgradeVersions != null)
                {
                    VersionRange versions = project.MajorUpgradeStrategy.UpgradeVersions;

                    var upgradeVersion = upgradeElement.AddElement(
                        new XElement("UpgradeVersion",
                           new XAttribute("Minimum", ExpandVersion(versions.Minimum)),
                           new XAttribute("IncludeMinimum", versions.IncludeMinimum.ToYesNo()),
                           new XAttribute("Maximum", ExpandVersion(versions.Maximum)),
                           new XAttribute("IncludeMaximum", versions.IncludeMaximum.ToYesNo()),
                           new XAttribute("Property", "UPGRADEFOUND")));

                    if (versions.MigrateFeatures.HasValue)
                        upgradeVersion.SetAttributeValue("MigrateFeatures", versions.MigrateFeatures.Value.ToYesNo());
                }

                if (project.MajorUpgradeStrategy.PreventDowngradingVersions != null)
                {
                    VersionRange versions = project.MajorUpgradeStrategy.PreventDowngradingVersions;

                    var upgradeVersion = upgradeElement.AddElement(
                        new XElement("UpgradeVersion",
                               new XAttribute("Minimum", ExpandVersion(versions.Minimum)),
                               new XAttribute("IncludeMinimum", versions.IncludeMinimum.ToYesNo()),
                               new XAttribute("OnlyDetect", "yes"),
                               new XAttribute("Property", "NEWPRODUCTFOUND")));

                    if (versions.MigrateFeatures.HasValue)
                        upgradeVersion.SetAttributeValue("MigrateFeatures", versions.MigrateFeatures.Value.ToYesNo());

                    var installExec = product.SelectOrCreate("InstallExecuteSequence");
                    var installUI = product.SelectOrCreate("InstallUISequence");

                    bool preventDowngrading = (project.MajorUpgradeStrategy.NewerProductInstalledErrorMessage != null);

                    if (preventDowngrading)
                    {
                        product.Add(new XElement("CustomAction",
                                        new XAttribute("Id", "PreventDowngrading"),
                                        new XAttribute("Error", project.MajorUpgradeStrategy.NewerProductInstalledErrorMessage)));

                        installExec.Add(new XElement("Custom", "NEWPRODUCTFOUND",
                                            new XAttribute("Action", "PreventDowngrading"),
                                            new XAttribute("After", "FindRelatedProducts")));

                        installUI.Add(new XElement("Custom", "NEWPRODUCTFOUND",
                                          new XAttribute("Action", "PreventDowngrading"),
                                          new XAttribute("After", "FindRelatedProducts")));
                    }

                    installExec.Add(new XElement("RemoveExistingProducts",
                                        new XAttribute("After", project.MajorUpgradeStrategy.RemoveExistingProductAfter.ToString())));
                }
            }
        }

        static Dictionary<string, Feature> autogeneratedShortcutLocations = new Dictionary<string, Feature>();

        static void ProcessDirectory(Dir wDir, Project wProject, Dictionary<Feature, List<string>> featureComponents,
            List<string> defaultFeatureComponents, List<string> autoGeneratedComponents, XElement parent)
        {
            XElement dirItem = AddDir(parent, wDir);

            if (wDir.Files.Count() == 0 && wDir.Shortcuts.Count() == 0 && wDir.Dirs.Count() == 0 && wDir.Permissions.Count() == 0)
            {
                var existingCompElement = dirItem.Elements("Component");

                if (existingCompElement.Count() == 0)
                {
                    string compId = wDir.Id + ".EmptyDirectory";

                    if (wDir.Feature != null)
                    {
                        featureComponents.Map(wDir.Feature, compId);
                    }
                    else
                    {
                        defaultFeatureComponents.Add(compId);
                    }

                    dirItem.AddElement(
                        new XElement("Component",
                            new XAttribute("Id", compId),
                            new XAttribute("Guid", WixGuid.NewGuid(compId))));
                }


                //insert MergeModules
                ProcessMergeModules(wDir, dirItem, featureComponents, defaultFeatureComponents);

                foreach (Dir subDir in wDir.Dirs)
                    ProcessDirectory(subDir, wProject, featureComponents, defaultFeatureComponents, autoGeneratedComponents, dirItem);

                return;
            }

            if (wDir.Feature != null)
            {
                string compId = "Component." + wDir.Id;
                featureComponents.Map(wDir.Feature, compId);
                dirItem.AddElement(
                        new XElement("Component",
                            new XAttribute("Id", compId),
                            new XAttribute("Guid", WixGuid.NewGuid(compId))));
            }

            #region Process Files

            //insert files in the last leaf directory node
            foreach (File wFile in wDir.Files)
            {
                string fileId = wFile.Id;
                string compId = "Component." + wFile.Id;

                if (wFile.Feature != null)
                {
                    featureComponents.Map(wFile.Feature, compId);
                }
                else
                {
                    defaultFeatureComponents.Add(compId);
                }

                XElement comp = dirItem.AddElement(
                    new XElement("Component",
                        new XAttribute("Id", compId),
                        new XAttribute("Guid", WixGuid.NewGuid(compId))));

                if (wFile.Condition != null)
                    comp.AddElement(
                        new XElement("Condition", new XCData(wFile.Condition.ToCData()))
                            .AddAttributes(wFile.Condition.Attributes));

                XElement file = comp.AddElement(
                    new XElement("File",
                        new XAttribute("Id", fileId),
                        new XAttribute("Source", Utils.PathCombine(wProject.SourceBaseDir, wFile.Name)))
                        .AddAttributes(wFile.Attributes));

                if (wFile.ServiceInstaller != null)
                    comp.Add(wFile.ServiceInstaller.ToXml(wProject));

                if (wFile is Assembly && (wFile as Assembly).RegisterInGAC)
                {
                    file.Add(new XAttribute("KeyPath", "yes"),
                             new XAttribute("Assembly", ".net"),
                             new XAttribute("AssemblyManifest", fileId),
                             new XAttribute("ProcessorArchitecture", ((Assembly)wFile).ProcessorArchitecture.ToString()));
                }

                //insert file associations
                foreach (FileAssociation wFileAssociation in wFile.Associations)
                {
                    XElement progId;
                    comp.Add(progId = new XElement("ProgId",
                                          new XAttribute("Id", wFileAssociation.Extension + ".file"),
                                          new XAttribute("Advertise", wFileAssociation.Advertise.ToYesNo()),
                                          new XAttribute("Description", wFileAssociation.Description),
                                          new XElement("Extension",
                                              new XAttribute("Id", wFileAssociation.Extension),
                                              new XAttribute("ContentType", wFileAssociation.ContentType),
                                              new XElement("Verb",
                                                  wFileAssociation.Advertise ?
                                                     new XAttribute("Sequence", wFileAssociation.SequenceNo) :
                                                     new XAttribute("TargetFile", fileId),
                                                  new XAttribute("Id", wFileAssociation.Command),
                                                  new XAttribute("Command", wFileAssociation.Command),
                                                  new XAttribute("Argument", wFileAssociation.Arguments)))));

                    if (wFileAssociation.Icon != null)
                    {
                        progId.Add(
                            new XAttribute("Icon", wFileAssociation.Icon != "" ? wFileAssociation.Icon : fileId),
                            new XAttribute("IconIndex", wFileAssociation.IconIndex));
                    }
                }

                //insert file owned shortcuts
                foreach (Shortcut wShortcut in wFile.Shortcuts)
                {
                    string locationDirId;

                    if (wShortcut.Location.IsEmpty())
                    {
                        locationDirId = wDir.Id;
                    }
                    else
                    {
                        Dir locationDir = wProject.FindDir(wShortcut.Location);

                        if (locationDir != null)
                        {
                            locationDirId = locationDir.Id;
                        }
                        else
                        {
                            if (!autogeneratedShortcutLocations.ContainsKey(wShortcut.Location))
                                autogeneratedShortcutLocations.Add(wShortcut.Location, wShortcut.Feature);

                            locationDirId = wShortcut.Location.Expand();
                        }
                    }

                    var shortcutElement =
                        new XElement("Shortcut",
                            new XAttribute("Id", "Shortcut." + wFile.Id + "." + wShortcut.Id),
                            new XAttribute("WorkingDirectory", !wShortcut.WorkingDirectory.IsEmpty() ? wShortcut.WorkingDirectory.Expand() : locationDirId),
                            new XAttribute("Directory", locationDirId),
                            new XAttribute("Name", wShortcut.Name.IsNullOrEmpty() ? IO.Path.GetFileNameWithoutExtension(wFile.Name) : wShortcut.Name + ".lnk"));

                    wShortcut.EmitAttributes(shortcutElement);

                    file.Add(shortcutElement);
                }

                //insert file related IIS virtual directories
                InsertIISElements(dirItem, comp, wFile.IISVirtualDirs, wProject);

                //insert file owned permissions
                ProcessFilePermissions(wProject, wFile, file);
            }

            #endregion

            #region Process Shorcuts

            //insert directory owned shortcuts
            foreach (Shortcut wShortcut in wDir.Shortcuts)
            {
                string compId = wShortcut.Id;
                if (wShortcut.Feature != null)
                {
                    if (!featureComponents.ContainsKey(wShortcut.Feature))
                        featureComponents[wShortcut.Feature] = new List<string>();

                    featureComponents[wShortcut.Feature].Add(compId);
                }
                else
                {
                    defaultFeatureComponents.Add(compId);
                }

                XElement comp = dirItem.AddElement(
                   new XElement("Component",
                       new XAttribute("Id", compId),
                       new XAttribute("Guid", WixGuid.NewGuid(compId))));

                if (wShortcut.Condition != null)
                    comp.AddElement(
                        new XElement("Condition", wShortcut.Condition.ToCData())
                            .AddAttributes(wShortcut.Condition.Attributes));

                XElement sc;
                sc = comp.AddElement(
                   new XElement("Shortcut",
                       new XAttribute("Id", wDir.Id + "." + wShortcut.Id),
                    //new XAttribute("Directory", wDir.Id), //not needed for Wix# as this attributed is required only if the shortcut is not nested under a Component element.
                       new XAttribute("WorkingDirectory", !wShortcut.WorkingDirectory.IsEmpty() ? wShortcut.WorkingDirectory.Expand() : GetShortcutWorkingDirectopry(wShortcut.Target)),
                       new XAttribute("Target", wShortcut.Target),
                       new XAttribute("Arguments", wShortcut.Arguments),
                       new XAttribute("Name", wShortcut.Name + ".lnk")));

                wShortcut.EmitAttributes(sc);
            }


            #endregion

            //insert MergeModules
            ProcessMergeModules(wDir, dirItem, featureComponents, defaultFeatureComponents);

            ProcessDirPermissions(wDir, wProject, featureComponents, defaultFeatureComponents, dirItem);

            foreach (Dir subDir in wDir.Dirs)
                ProcessDirectory(subDir, wProject, featureComponents, defaultFeatureComponents, autoGeneratedComponents, dirItem);
        }

        private static void ProcessFilePermissions(Project wProject, File wFile, XElement file)
        {
            if (wFile.Permissions.Any())
            {
                var utilExtension = WixExtension.Util;
                wProject.IncludeWixExtension(utilExtension);

                foreach (var permission in wFile.Permissions)
                {
                    var element = new XElement(utilExtension.ToXNamespace() + "PermissionEx");
                    permission.EmitAttributes(element);
                    file.Add(element);
                }
            }
        }

        private static void ProcessDirPermissions(Dir wDir, Project wProject, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement dirItem)
        {
            if (wDir.Permissions.Any())
            {
                var utilExtension = WixExtension.Util;
                wProject.IncludeWixExtension(utilExtension);

                foreach (var permission in wDir.Permissions)
                {
                    string compId = "Component" + permission.Id;
                    if (permission.Feature != null)
                    {
                        if (!featureComponents.ContainsKey(permission.Feature))
                            featureComponents[permission.Feature] = new List<string>();

                        featureComponents[permission.Feature].Add(compId);
                    }
                    else
                    {
                        defaultFeatureComponents.Add(compId);
                    }

                    var permissionElement = new XElement(utilExtension.ToXNamespace() + "PermissionEx");
                    permission.EmitAttributes(permissionElement);
                    dirItem.Add(
                        new XElement("Component",
                            new XAttribute("Id", compId),
                            new XAttribute("Guid", WixGuid.NewGuid(compId)),
                            new XElement("CreateFolder",
                                permissionElement)));
                }
            }
        }

        static void ProcessMergeModules(Dir wDir, XElement dirItem, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents)
        {
            foreach (Merge msm in wDir.MergeModules)
            {
                XElement media = dirItem.Parent("Product").Select("Media");
                XElement package = dirItem.Parent("Product").Select("Package");

                string language = package.Attribute("Languages").Value; //note Wix# expects package.Attribute("Languages") to have a single value (yest it is a temporary limitation)
                string diskId = media.Attribute("Id").Value;

                XElement merge = dirItem.AddElement(
                    new XElement("Merge",
                        new XAttribute("Id", msm.Id),
                        new XAttribute("FileCompression", msm.FileCompression.ToYesNo()),
                        new XAttribute("Language", language),
                        new XAttribute("SourceFile", msm.SourceFile),
                        new XAttribute("DiskId", diskId))
                        .AddAttributes(msm.Attributes));

                if (!featureComponents.ContainsKey(msm.Feature))
                    featureComponents[msm.Feature] = new List<string>();

                //currently WiX does not allow child Condition element but in the future release it most likely will
                //if (msm.Condition != null)
                //    merge.AddElement(
                //        new XElement("Condition", new XCData(msm.Condition.ToCData()))
                //            .AddAttributes(msm.Condition.Attributes));
            }
        }

        static void PostProcessMsm(Project project, XElement product)
        {
            var modules = from dir in project.AllDirs
                          from msm in dir.MergeModules
                          select new
                          {
                              Feature = msm.Feature,
                              MsmId = msm.Id
                          };

            var features = (from f in product.Elements("Feature")
                            select f)
                           .ToDictionary(x => x.Attribute("Id").Value);

            foreach (var item in modules)
            {
                XElement xFeature;

                if (item.Feature == null)
                {
                    if (features.ContainsKey("Complete"))
                        xFeature = features["Complete"];
                    else
                        throw new Exception("Merge Module " + item.MsmId + " does not belong to any feature and \"Complete\" feature is not found");
                }
                else
                {
                    if (features.ContainsKey(item.Feature.Id))
                        xFeature = features[item.Feature.Id];
                    else
                        xFeature = product.AddElement(
                                             new XElement("Feature",
                                                 new XAttribute("Id", item.Feature.Id),
                                                 new XAttribute("Title", item.Feature.Name),
                                                 new XAttribute("Absent", item.Feature.AllowChange ? "allow" : "disallow"),
                                                 new XAttribute("Level", item.Feature.IsEnabled ? "1" : "2"))
                                                 .AddAttributes(item.Feature.Attributes));
                }

                xFeature.Add(new XElement("MergeRef",
                                 new XAttribute("Id", item.MsmId)));
            }
        }

        static void ProcessDirectories(Project wProject, Dictionary<Feature, List<string>> featureComponents,
            List<string> defaultFeatureComponents, List<string> autoGeneratedComponents, XElement dirs)
        {
            wProject.ResolveWildCards();

            if (wProject.Dirs.Count() == 0)
            {
                //WIX/MSI does not like no-directory deployments thus create fake one
                string dummyDir = @"%ProgramFiles%\WixSharp\DummyDir";
                if (wProject.Platform == Platform.x64)
                    dummyDir = dummyDir.Map64Dirs();

                wProject.Dirs = new[] { new Dir(dummyDir) };
            }

            Dir[] wDirs = wProject.Dirs;

            //auto-assign INSTALLDIR id for installation directory (the first directory that has multiple items)
            if (wDirs.Count() != 0)
            {
                Dir firstDirWithItems = wDirs.First();

                string logicalPath = firstDirWithItems.Name;
                while (firstDirWithItems.Shortcuts.Count() == 0 &&
                       firstDirWithItems.Dirs.Count() == 1 &&
                       firstDirWithItems.Files.Count() == 0)
                {
                    firstDirWithItems = firstDirWithItems.Dirs.First();
                    logicalPath += "\\" + firstDirWithItems.Name;
                }

                if (!firstDirWithItems.IsIdSet() && !Compiler.AutoGeneration.InstallDirDefaultId.IsEmpty())
                {
                    firstDirWithItems.Id = Compiler.AutoGeneration.InstallDirDefaultId;
                    wProject.AutoAssignedInstallDirPath = logicalPath;
                }
            }

            foreach (Dir wDir in wDirs)
            {
                ProcessDirectory(wDir, wProject, featureComponents, defaultFeatureComponents, autoGeneratedComponents, dirs);
            }

            foreach (string dirPath in autogeneratedShortcutLocations.Keys)
            {
                Feature feature = autogeneratedShortcutLocations[dirPath];

                //be careful as some parts of the auto-generated director may already exist
                XElement existingParent = null;
                string dirToAdd = dirPath;
                string[] dirsToSearch = Dir.ToFlatPathTree(dirPath);
                foreach (string path in dirsToSearch)
                {
                    Dir d = wProject.FindDir(path);
                    if (d != null)
                    {
                        dirToAdd = dirPath.Substring(path.Length + 1);
                        existingParent = dirs.FindDirectory(path.ExpandWixEnvConsts());
                        break;
                    }
                }

                if (existingParent != null)
                {
                    Dir dir = new Dir(feature, dirToAdd);
                    ProcessDirectory(dir, wProject, featureComponents, defaultFeatureComponents, autoGeneratedComponents, existingParent);
                }
                else
                {
                    Dir dir = new Dir(feature, dirPath);
                    ProcessDirectory(dir, wProject, featureComponents, defaultFeatureComponents, autoGeneratedComponents, dirs);
                }
            }
        }

        static void ProcessRegKeys(Project wProject, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product)
        {
            //From Wix documentation it is not clear how to have RegistryKey outside of the Directory element
            //thus let's use the top level directory element for the stand alone registry collection
            if (wProject.RegValues.Length != 0)
            {
                var count = 0;
                var keyPathSet = false;
                foreach (RegValue regVal in wProject.RegValues)
                {
                    if (regVal.Win64)
                    {
                        regVal.AttributesDefinition += ";Component:Win64=yes";
                    }

                    count++;
                    string compId = "Registry" + count;

                    //all registry of this level belong to the same component
                    if (regVal.Feature != null)
                    {
                        if (!featureComponents.ContainsKey(regVal.Feature))
                            featureComponents[regVal.Feature] = new List<string>();

                        featureComponents[regVal.Feature].Add(compId);
                    }
                    else
                    {
                        defaultFeatureComponents.Add(compId);
                    }

                    XElement topLevelDir = GetTopLevelDir(product);
                    XElement comp = topLevelDir.AddElement(
                                                   new XElement("Component",
                                                       new XAttribute("Id", compId),
                                                       new XAttribute("Guid", WixGuid.NewGuid(compId))));

                    if (regVal.Condition != null)
                        comp.AddElement(
                            new XElement("Condition", regVal.Condition.ToCData())
                                .AddAttributes(regVal.Condition.Attributes));

                    XElement regValEl;
                    XElement regKeyEl;
                    regKeyEl = comp.AddElement(
                            new XElement("RegistryKey",
                                new XAttribute("Root", regVal.Root.ToWString()),
                                regValEl = new XElement("RegistryValue",
                                               new XAttribute("Type", regVal.RegTypeString),
                                               new XAttribute("KeyPath", keyPathSet.ToYesNo()))
                                               .AddAttributes(regVal.Attributes)));
                    if (!regVal.Key.IsEmpty())
                        regKeyEl.Add(new XAttribute("Key", regVal.Key));

                    string stringValue = regVal.RegValueString.ExpandWixEnvConsts();
                    if (regValEl.Attribute("Type").Value == "multiString")
                    {
                        foreach (string line in stringValue.GetLines())
                            regValEl.Add(new XElement("MultiStringValue", line));
                    }
                    else
                        regValEl.Add(new XAttribute("Value", stringValue));

                    if (regVal.RegistryKeyAction != RegistryKeyAction.none)
                    {
                        regKeyEl.Add(new XAttribute("Action", regVal.RegistryKeyAction.ToString()));
                    }
                    if (regVal.ForceCreateOnInstall)
                    {
                        regKeyEl.Add(new XAttribute("ForceCreateOnInstall", regVal.ForceCreateOnInstall.ToYesNo()));
                    }
                    if (regVal.ForceDeleteOnUninstall)
                    {
                        regKeyEl.Add(new XAttribute("ForceDeleteOnUninstall", regVal.ForceDeleteOnUninstall.ToYesNo()));
                    }
                    if (regVal.Name != "")
                        regValEl.Add(new XAttribute("Name", regVal.Name));

                    keyPathSet = true;
                }
            }
        }

        static void ProcessEnvVars(Project wProject, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product)
        {
            //From Wix documentation it is not clear how to have EnvironmentVariable outside of the Directory element
            //thus let's use the top level directory element for the stand alone registry collection
            var count = 0;
            foreach (EnvironmentVariable envVar in wProject.EnvironmentVariables)
            {
                count++;
                string compId = "EnvVars" + count;

                //all registry of this level belong to the same component
                if (envVar.Feature != null)
                {
                    if (!featureComponents.ContainsKey(envVar.Feature))
                        featureComponents[envVar.Feature] = new List<string>();

                    featureComponents[envVar.Feature].Add(compId);
                }
                else
                {
                    defaultFeatureComponents.Add(compId);
                }

                XElement topLevelDir = GetTopLevelDir(product);
                XElement comp = topLevelDir.AddElement(
                                               new XElement("Component",
                                                   new XAttribute("Id", compId),
                                                   new XAttribute("Guid", WixGuid.NewGuid(compId))));

                if (envVar.Condition != null)
                    comp.AddElement(
                        new XElement("Condition", envVar.Condition.ToCData())
                            .AddAttributes(envVar.Condition.Attributes));

                comp.Add(envVar.ToXml());
            }
        }

        static void ProcessUsers(Project project, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product)
        {
            if (!project.Users.Any()) return;

            project.IncludeWixExtension(WixExtension.Util);

            int componentCount = 0;
            foreach (var user in project.Users)
            {
                //the user definition is an installed component, not only a reference
                if (user.MustDescendFromComponent)
                {
                    componentCount++;
                    var compId = "User" + componentCount;

                    if (user.Feature != null)
                    {
                        if (!featureComponents.ContainsKey(user.Feature))
                            featureComponents[user.Feature] = new List<string>();

                        featureComponents[user.Feature].Add(compId);
                    }
                    else
                    {
                        defaultFeatureComponents.Add(compId);
                    }

                    //anchoring the generated component to the top level directory
                    var topLevelDir = GetTopLevelDir(product);

                    var userComponent =
                        topLevelDir.AddElement(
                            new XElement("Component",
                                new XAttribute("Id", compId),
                                new XAttribute("Guid", WixGuid.NewGuid(compId))));

                    var userElement = new XElement(WixExtension.Util.ToXNamespace() + "User");
                    user.EmitAttributes(userElement);

                    userComponent.Add(userElement);
                }
                //the user definition is a reference, only
                else
                {
                    var userElement = new XElement(WixExtension.Util.ToXNamespace() + "User");
                    user.EmitAttributes(userElement);
                    product.Add(userElement);
                }
            }
        }

        private static void ProcessSql(Project project, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product)
        {
            if (!project.SqlDatabases.Any()) return;

            project.IncludeWixExtension(WixExtension.Sql);

            int componentCount = 0;
            foreach (var sqlDb in project.SqlDatabases)
            {
                if (sqlDb.MustDescendFromComponent)
                {
                    componentCount++;
                    var compId = "SqlDatabase" + componentCount;

                    if (sqlDb.Feature != null)
                    {
                        if (!featureComponents.ContainsKey(sqlDb.Feature))
                            featureComponents[sqlDb.Feature] = new List<string>();
                        featureComponents[sqlDb.Feature].Add(compId);
                    }
                    else defaultFeatureComponents.Add(compId);

                    //anchoring the generated component to the top level directory
                    var topLevelDir = GetTopLevelDir(product);

                    var sqlDbComponent =
                        topLevelDir.AddElement(
                            new XElement("Component",
                                new XAttribute("Id", compId),
                                new XAttribute("Guid", WixGuid.NewGuid(compId))));

                    var sqlDbElement = new XElement(WixExtension.Sql.ToXNamespace() + "SqlDatabase");
                    sqlDb.EmitAttributes(sqlDbElement);

                    foreach (var sqlString in sqlDb.SqlStrings)
                    {
                        var element = new XElement(WixExtension.Sql.ToXNamespace() + "SqlString");
                        sqlString.EmitAttributes(element);
                        sqlDbElement.Add(element);
                    }

                    foreach (var sqlScript in sqlDb.SqlScripts)
                    {
                        var element = new XElement(WixExtension.Sql.ToXNamespace() + "SqlScript");
                        sqlScript.EmitAttributes(element);
                        sqlDbElement.Add(element);
                    }

                    sqlDbComponent.Add(sqlDbElement);
                }
                else
                {
                    var sqlDbElement = new XElement(WixExtension.Sql.ToXNamespace() + "SqlDatabase");
                    sqlDb.EmitAttributes(sqlDbElement);

                    ProcessSqlStrings(featureComponents, defaultFeatureComponents, product, sqlDb);
                    ProcessSqlScripts(featureComponents, defaultFeatureComponents, product, sqlDb);

                    product.Add(sqlDbElement);
                }
            }
        }

        private static void ProcessSqlScripts(Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product, SqlDatabase sqlDb)
        {
            int scriptCount = 0;
            foreach (var sqlScript in sqlDb.SqlScripts)
            {
                sqlScript.SqlDb = sqlDb.Id;

                scriptCount++;
                var compId = "SqlScript" + scriptCount;

                if (sqlScript.Feature != null)
                {
                    if (!featureComponents.ContainsKey(sqlScript.Feature))
                        featureComponents[sqlScript.Feature] = new List<string>();
                    featureComponents[sqlScript.Feature].Add(compId);
                }
                else defaultFeatureComponents.Add(compId);

                //anchoring the generated component to the top level directory
                var topLevelDir = GetTopLevelDir(product);

                var sqlScriptComponent =
                    topLevelDir.AddElement(
                        new XElement("Component",
                            new XAttribute("Id", compId),
                            new XAttribute("Guid", WixGuid.NewGuid(compId))));

                var sqlScriptElement = new XElement(WixExtension.Sql.ToXNamespace() + "SqlScript");
                sqlScript.EmitAttributes(sqlScriptElement);

                sqlScriptComponent.Add(sqlScriptElement);
            }
        }

        private static void ProcessSqlStrings(Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product, SqlDatabase sqlDb)
        {
            int stringCount = 0;
            foreach (var sqlString in sqlDb.SqlStrings)
            {
                sqlString.SqlDb = sqlDb.Id;

                stringCount++;
                var compId = "SqlString" + stringCount;

                if (sqlString.Feature != null)
                {
                    if (!featureComponents.ContainsKey(sqlString.Feature))
                        featureComponents[sqlString.Feature] = new List<string>();
                    featureComponents[sqlString.Feature].Add(compId);
                }
                else defaultFeatureComponents.Add(compId);

                //anchoring the generated component to the top level directory
                var topLevelDir = GetTopLevelDir(product);

                var sqlStringComponent =
                    topLevelDir.AddElement(
                        new XElement("Component",
                            new XAttribute("Id", compId),
                            new XAttribute("Guid", WixGuid.NewGuid(compId))));

                var sqlStringElement = new XElement(WixExtension.Sql.ToXNamespace() + "SqlString");
                sqlString.EmitAttributes(sqlStringElement);

                sqlStringComponent.Add(sqlStringElement);
            }
        }

        private static void ProcessCertificates(Project project, Dictionary<Feature, List<string>> featureComponents, List<string> defaultFeatureComponents, XElement product)
        {
            if (!project.Certificates.Any()) return;

            project.IncludeWixExtension(WixExtension.IIs);

            int componentCount = 0;
            foreach (var certificate in project.Certificates)
            {
                componentCount++;

                var compId = "Certificate" + componentCount;

                if (certificate.Feature != null)
                {
                    if (!featureComponents.ContainsKey(certificate.Feature))
                        featureComponents[certificate.Feature] = new List<string>();

                    featureComponents[certificate.Feature].Add(compId);
                }
                else
                {
                    defaultFeatureComponents.Add(compId);
                }

                var topLevelDir = GetTopLevelDir(product);
                var comp = topLevelDir.AddElement(
                               new XElement("Component",
                                   new XAttribute("Id", compId),
                                   new XAttribute("Guid", WixGuid.NewGuid(compId))));

                comp.Add(certificate.ToXml());
            }
        }

        static void ProcessLaunchConditions(Project project, XElement product)
        {
            foreach (var condition in project.LaunchConditions)
                product.Add(new XElement("Condition",
                                new XAttribute("Message", condition.Message),
                                condition.ToCData())
                                .AddAttributes(condition.Attributes));
        }

        static void InsertWebSite(WebSite webSite, string dirID, XElement element)
        {
            XNamespace ns = "http://schemas.microsoft.com/wix/IIsExtension";

            var id = webSite.Name.Expand();
            XElement xWebSite = element.AddElement(new XElement(ns + "WebSite",
                                                   new XAttribute("Id", id),
                                                   new XAttribute("Description", webSite.Description),
                                                   new XAttribute("Directory", dirID)));

            xWebSite.AddAttributes(webSite.Attributes);

            foreach (WebSite.WebAddress address in webSite.Addresses)
            {
                XElement xAddress = xWebSite.AddElement(new XElement(ns + "WebAddress",
                                                            new XAttribute("Id", address.Address == "*" ? "AllUnassigned" : address.Address),
                                                            new XAttribute("Port", address.Port)));

                xAddress.AddAttributes(address.Attributes);
            }
        }

        static void InsertIISElements(XElement dirItem, XElement component, IISVirtualDir[] wVDirs, Project project)
        {
            //http://ranjithk.com/2009/12/17/automating-web-deployment-using-windows-installer-xml-wix/

            XNamespace ns = "http://schemas.microsoft.com/wix/IIsExtension";

            string dirID = dirItem.Attribute("Id").Value;
            var xProduct = component.Parent("Product");

            var uniqueWebSites = new List<WebSite>();

            bool wasInserted = true;
            foreach (IISVirtualDir wVDir in wVDirs)
            {
                wasInserted = true;

                XElement xWebApp;
                var xVDir = component.AddElement(new XElement(ns + "WebVirtualDir",
                                                     new XAttribute("Id", wVDir.Name.Expand()),
                                                     new XAttribute("Alias", wVDir.Alias.IsEmpty() ? wVDir.Name : wVDir.Alias),
                                                     new XAttribute("Directory", dirID),
                                                     new XAttribute("WebSite", wVDir.WebSite.Name.Expand()),
                                                     xWebApp = new XElement(ns + "WebApplication",
                                                         new XAttribute("Id", wVDir.AppName.Expand() + "WebApplication"),
                                                         new XAttribute("Name", wVDir.AppName))));
                if (wVDir.AllowSessions.HasValue)
                    xWebApp.Add(new XAttribute("AllowSessions", wVDir.AllowSessions.Value.ToYesNo()));
                if (wVDir.Buffer.HasValue)
                    xWebApp.Add(new XAttribute("Buffer", wVDir.Buffer.Value.ToYesNo()));
                if (wVDir.ClientDebugging.HasValue)
                    xWebApp.Add(new XAttribute("ClientDebugging", wVDir.ClientDebugging.Value.ToYesNo()));
                if (wVDir.DefaultScript.HasValue)
                    xWebApp.Add(new XAttribute("DefaultScript", wVDir.DefaultScript.Value.ToString()));
                if (wVDir.Isolation.HasValue)
                    xWebApp.Add(new XAttribute("Isolation", wVDir.Isolation.Value.ToString()));
                if (wVDir.ParentPaths.HasValue)
                    xWebApp.Add(new XAttribute("ParentPaths", wVDir.ParentPaths.Value.ToYesNo()));
                if (wVDir.ScriptTimeout.HasValue)
                    xWebApp.Add(new XAttribute("ScriptTimeout", wVDir.ScriptTimeout.Value));
                if (wVDir.ServerDebugging.HasValue)
                    xWebApp.Add(new XAttribute("ServerDebugging", wVDir.ServerDebugging.Value.ToYesNo()));
                if (wVDir.SessionTimeout.HasValue)
                    xWebApp.Add(new XAttribute("SessionTimeout", wVDir.SessionTimeout.Value));

                //do not create WebSite on IIS but install WebApp into existing
                if (!wVDir.WebSite.InstallWebSite)
                {
                    if (!uniqueWebSites.Contains(wVDir.WebSite))
                        uniqueWebSites.Add(wVDir.WebSite);
                }
                else
                {
                    InsertWebSite(wVDir.WebSite, dirID, component);
                }

                if (wVDir.WebAppPool != null)
                {
                    var id = wVDir.Name.Expand() + "_AppPool";

                    xWebApp.Add(new XAttribute("WebAppPool", id));

                    var xAppPool = component.AddElement(new XElement(ns + "WebAppPool",
                                                            new XAttribute("Id", id),
                                                            new XAttribute("Name", wVDir.WebAppPool.Name)));

                    xAppPool.AddAttributes(wVDir.WebAppPool.Attributes);
                }

                if (wVDir.WebDirProperties != null)
                {
                    var propId = wVDir.Name.Expand() + "_WebDirProperties";

                    var xDirProp = xProduct.AddElement(new XElement(ns + "WebDirProperties",
                                                           new XAttribute("Id", propId)));

                    xDirProp.AddAttributes(wVDir.WebDirProperties.Attributes);

                    xVDir.Add(new XAttribute("DirProperties", propId));
                }
            }

            foreach (WebSite webSite in uniqueWebSites)
            {
                InsertWebSite(webSite, dirID, xProduct);
            }

            if (wasInserted)
            {
                if (!project.WixExtensions.Contains("WixIIsExtension.dll"))
                    project.WixExtensions.Add("WixIIsExtension.dll");

                if (!project.WixNamespaces.Contains("xmlns:iis=\"http://schemas.microsoft.com/wix/IIsExtension\""))
                    project.WixNamespaces.Add("xmlns:iis=\"http://schemas.microsoft.com/wix/IIsExtension\"");
            }
        }

        static void ProcessProperties(Project wProject, XElement product)
        {
            foreach (var prop in wProject.Properties)
            {
                if (prop is PropertyRef)
                {
                    var propRef = (prop as PropertyRef);

                    if (propRef.Id.IsEmpty())
                        throw new Exception("'" + typeof(PropertyRef).Name + "'.Id must be set before compiling the project.");

                    product.Add(new XElement("PropertyRef",
                                    new XAttribute("Id", propRef.Id)));
                }
                else if (prop is RegValueProperty)
                {
                    var rvProp = (prop as RegValueProperty);

                    XElement RegistrySearchElement;
                    XElement xProp = product.AddElement(
                                new XElement("Property",
                                    new XAttribute("Id", rvProp.Name),
                                    RegistrySearchElement = new XElement("RegistrySearch",
                                        new XAttribute("Id", rvProp.Name + "_RegSearch"),
                                        new XAttribute("Root", rvProp.Root.ToWString()),
                                        new XAttribute("Key", rvProp.Key),
                                        new XAttribute("Type", "raw")
                                        ))
                                    .AddAttributes(rvProp.Attributes));

                    if (!rvProp.Value.IsEmpty())
                        xProp.Add(new XAttribute("Value", rvProp.Value));

                    if (rvProp.EntryName != "")
                        RegistrySearchElement.Add(new XAttribute("Name", rvProp.EntryName));
                }
                else
                {
                    product.Add(new XElement("Property",
                                    new XAttribute("Id", prop.Name),
                                    new XAttribute("Value", prop.Value))
                                    .AddAttributes(prop.Attributes));
                }
            }
        }

        static void ProcessBinaries(Project wProject, XElement product)
        {
            foreach (var bin in wProject.Binaries)
            {
                string bynaryKey = bin.Id;
                string bynaryPath = bin.Name;

                if (bin is EmbeddedAssembly)
                {
                    var asmBin = bin as EmbeddedAssembly;

                    bynaryPath = asmBin.Name.PathChangeDirectory(wProject.OutDir.PathGetFullPath())
                                            .PathChangeExtension(".CA.dll");

                    PackageManagedAsm(asmBin.Name, bynaryPath, asmBin.RefAssemblies.Concat(wProject.DefaultRefAssemblies).Distinct().ToArray(), wProject.OutDir, wProject.CustomActionConfig);
                }

                product.Add(new XElement("Binary",
                                new XAttribute("Id", bynaryKey),
                                new XAttribute("SourceFile", bynaryPath))
                                .AddAttributes(bin.Attributes));
            }
        }

        /// <summary>
        /// Processes the custom actions.
        /// </summary>
        /// <param name="wProject">The w project.</param>
        /// <param name="product">The product.</param>
        /// <exception cref="System.Exception">Step.PreviousAction is specified for the very first 'Custom Action'.\nThere cannot be any previous action as it is the very first one in the sequence.</exception>
        static void ProcessCustomActions(Project wProject, XElement product)
        {
            string lastActionName = null;

            foreach (Action wAction in wProject.Actions)
            {
                string step = wAction.Step.ToString();

                if (wAction.When == When.After && wAction.Step == Step.PreviousAction)
                {
                    if (lastActionName == null)
                        throw new Exception("Step.PreviousAction is specified for the very first 'Custom Action'.\nThere cannot be any previous action as it is the very first one in the sequence.");
                    step = lastActionName;
                }
                else if (wAction.When == When.After && wAction.Step == Step.PreviousActionOrInstallFinalize)
                    step = lastActionName ?? Step.InstallFinalize.ToString();
                else if (wAction.When == When.After && wAction.Step == Step.PreviousActionOrInstallInitialize)
                    step = lastActionName ?? Step.InstallInitialize.ToString();

                lastActionName = wAction.Name.Expand();

                List<XElement> sequences = new List<XElement>();

                if (wAction.Sequence != Sequence.NotInSequence)
                {
                    foreach (var item in wAction.Sequence.GetValues())
                        sequences.Add(product.SelectOrCreate(item));
                }

                XAttribute sequenceNumberAttr = wAction.SequenceNumber.HasValue ?
                                                    new XAttribute("Sequence", wAction.SequenceNumber.Value) :
                                                    new XAttribute(wAction.When.ToString(), step);

                if (wAction is SetPropertyAction)
                {
                    var wSetPropAction = (SetPropertyAction)wAction;

                    var actionId = wSetPropAction.Id;
                    lastActionName = actionId; //overwrite previously set standard CA name

                    product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", actionId),
                            new XAttribute("Property", wSetPropAction.PropName),
                            new XAttribute("Value", wSetPropAction.Value))
                            .AddAttributes(wSetPropAction.Attributes));

                    sequences.ForEach(sequence =>
                        sequence.Add(new XElement("Custom", wAction.Condition.ToString(),
                                         new XAttribute("Action", actionId),
                                         sequenceNumberAttr)));
                }
                else if (wAction is ScriptFileAction)
                {
                    var wScriptAction = (ScriptFileAction)wAction;

                    sequences.ForEach(sequence =>
                         sequence.Add(new XElement("Custom", wAction.Condition.ToString(),
                                          new XAttribute("Action", wAction.Id),
                                          sequenceNumberAttr)));

                    product.Add(new XElement("Binary",
                                    new XAttribute("Id", wAction.Name.Expand() + "_File"),
                                    new XAttribute("SourceFile", Utils.PathCombine(wProject.SourceBaseDir, wScriptAction.ScriptFile))));

                    product.Add(new XElement("CustomAction",
                                    new XAttribute("Id", wAction.Name.Expand()),
                                    new XAttribute("BinaryKey", wAction.Name.Expand() + "_File"),
                                    new XAttribute("VBScriptCall", wScriptAction.Procedure),
                                    new XAttribute("Return", wAction.Return))
                                    .AddAttributes(wAction.Attributes));
                }
                else if (wAction is ScriptAction)
                {
                    var wScriptAction = (ScriptAction)wAction;

                    sequences.ForEach(sequence =>
                        sequence.Add(new XElement("Custom", wAction.Condition.ToString(),
                                       new XAttribute("Action", wAction.Id),
                                       sequenceNumberAttr)));

                    product.Add(new XElement("CustomAction",
                                    new XCData(wScriptAction.Code),
                                    new XAttribute("Id", wAction.Name.Expand()),
                                    new XAttribute("Script", "vbscript"),
                                    new XAttribute("Return", wAction.Return))
                                    .AddAttributes(wAction.Attributes));
                }
                else if (wAction is ManagedAction)
                {
                    var wManagedAction = (ManagedAction)wAction;
                    var asmFile = Utils.PathCombine(wProject.SourceBaseDir, wManagedAction.ActionAssembly);
                    var packageFile = asmFile.PathChangeDirectory(wProject.OutDir.PathGetFullPath())
                                             .PathChangeExtension(".CA.dll");

                    var existingBinary = product.Descendants("Binary")
                                                .Where(e => e.Attribute("SourceFile").Value == packageFile)
                                                .FirstOrDefault();

                    string bynaryKey;

                    if (existingBinary == null)
                    {
                        PackageManagedAsm(
                            asmFile,
                            packageFile,
                            wManagedAction.RefAssemblies.Concat(wProject.DefaultRefAssemblies).Distinct().ToArray(),
                            wProject.OutDir,
                            wProject.CustomActionConfig,
                            wProject.Platform);

                        bynaryKey = wAction.Name.Expand() + "_File";
                        product.Add(new XElement("Binary",
                                        new XAttribute("Id", bynaryKey),
                                        new XAttribute("SourceFile", packageFile)));
                    }
                    else
                    {
                        bynaryKey = existingBinary.Attribute("Id").Value;
                    }

                    if (wManagedAction.Execute == Execute.deferred && wManagedAction.UsesProperties != null) //map managed action properties
                    {
                        string mapping = wManagedAction.ExpandAllUsedProperties();

                        if (!mapping.IsEmpty())
                        {
                            var setPropValuesId = "Set_" + wAction.Id + "_Props";

                            product.Add(new XElement("CustomAction",
                                            new XAttribute("Id", setPropValuesId),
                                            new XAttribute("Property", wAction.Id),
                                            new XAttribute("Value", mapping)));

                            sequences.ForEach(sequence =>
                                sequence.Add(
                                    new XElement("Custom",
                                        new XAttribute("Action", setPropValuesId),
                                        wAction.SequenceNumber.HasValue ?
                                            new XAttribute("Sequence", wAction.SequenceNumber.Value) :
                                            new XAttribute("After", "InstallInitialize"))));
                        }
                    }

                    sequences.ForEach(sequence =>
                        sequence.Add(new XElement("Custom", wAction.Condition.ToString(),
                                         new XAttribute("Action", wAction.Id),
                                         sequenceNumberAttr)));

                    product.Add(new XElement("CustomAction",
                                    new XAttribute("Id", wAction.Id),
                                    new XAttribute("BinaryKey", bynaryKey),
                                    new XAttribute("DllEntry", wManagedAction.MethodName),
                                    new XAttribute("Impersonate", wAction.Impersonate.ToYesNo()),
                                    new XAttribute("Execute", wAction.Execute),
                                    new XAttribute("Return", wAction.Return))
                                    .AddAttributes(wAction.Attributes));
                }
                else if (wAction is QtCmdLineAction)
                {
                    var cmdLineAction = (QtCmdLineAction)wAction;
                    var cmdLineActionId = wAction.Name.Expand();
                    var setCmdLineActionId = "Set_" + cmdLineActionId + "_CmdLine";

                    product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", setCmdLineActionId),
                            new XAttribute("Property", "QtExecCmdLine"),
                            new XAttribute("Value", "\"" + cmdLineAction.AppPath.ExpandCommandPath() + "\" " + cmdLineAction.Args.ExpandCommandPath()))
                            .AddAttributes(cmdLineAction.Attributes));

                    product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", cmdLineActionId),
                            new XAttribute("BinaryKey", "WixCA"),
                            new XAttribute("DllEntry", "CAQuietExec"),
                            new XAttribute("Impersonate", wAction.Impersonate.ToYesNo()),
                            new XAttribute("Execute", wAction.Execute),
                            new XAttribute("Return", wAction.Return)));

                    lastActionName = cmdLineActionId;

                    sequences.ForEach(sequence =>
                        sequence.Add(
                            new XElement("Custom", wAction.Condition.ToString(),
                                new XAttribute("Action", setCmdLineActionId),
                                sequenceNumberAttr)));

                    sequences.ForEach(sequence =>
                        sequence.Add(
                            new XElement("Custom", wAction.Condition.ToString(),
                                new XAttribute("Action", cmdLineActionId),
                                new XAttribute("After", setCmdLineActionId))));

                    var extensionAssembly = Utils.PathCombine(WixLocation, @"WixUtilExtension.dll");
                    if (wProject.WixExtensions.Find(x => x == extensionAssembly) == null)
                        wProject.WixExtensions.Add(extensionAssembly);
                }
                else if (wAction is InstalledFileAction)
                {
                    var fileAction = (InstalledFileAction)wAction;

                    sequences.ForEach(sequence =>
                        sequence.Add(
                            new XElement("Custom", wAction.Condition.ToString(),
                                new XAttribute("Action", wAction.Id),
                                sequenceNumberAttr)));

                    var actionElement = product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", wAction.Name.Expand()),
                            new XAttribute("ExeCommand", fileAction.Args.ExpandCommandPath()),
                            new XAttribute("Return", wAction.Return))
                            .AddAttributes(wAction.Attributes));

                    actionElement.Add(new XAttribute("FileKey", fileAction.Key));
                }
                else if (wAction is BinaryFileAction)
                {
                    var binaryAction = (BinaryFileAction)wAction;

                    sequences.ForEach(sequence =>
                        sequence.Add(
                            new XElement("Custom", wAction.Condition.ToString(),
                                new XAttribute("Action", wAction.Id),
                                sequenceNumberAttr)));

                    var actionElement = product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", wAction.Name.Expand()),
                            new XAttribute("ExeCommand", binaryAction.Args.ExpandCommandPath()),
                            new XAttribute("Impersonate", wAction.Impersonate.ToYesNo()),
                            new XAttribute("Execute", wAction.Execute),
                            new XAttribute("Return", wAction.Return))
                            .AddAttributes(wAction.Attributes));

                    actionElement.Add(new XAttribute("BinaryKey", binaryAction.Key));
                }
                else if (wAction is PathFileAction)
                {
                    var fileAction = (PathFileAction)wAction;

                    sequences.ForEach(sequence =>
                        sequence.Add(
                            new XElement("Custom", fileAction.Condition.ToString(),
                                new XAttribute("Action", fileAction.Id),
                                sequenceNumberAttr)));

                    var actionElement = product.AddElement(
                        new XElement("CustomAction",
                            new XAttribute("Id", fileAction.Name.Expand()),
                            new XAttribute("ExeCommand", "\"" + fileAction.AppPath.ExpandCommandPath() + "\" " + fileAction.Args.ExpandCommandPath()),
                            new XAttribute("Return", wAction.Return))
                            .AddAttributes(fileAction.Attributes));

                    Dir installedDir = Array.Find(wProject.Dirs, (x) => x.Name == fileAction.WorkingDir);
                    if (installedDir != null)
                        actionElement.Add(new XAttribute("Directory", installedDir.Id));
                    else
                        actionElement.Add(new XAttribute("Directory", fileAction.WorkingDir.Expand()));
                }
            }
        }

        static string clientAssembly;

        /// <summary>
        /// Path to the <c>WixSharp.dll</c> client assembly. Typically it is the Wix# setup script assembly.
        /// <para>This value is used to resolve <c>%this%</c> of the <see cref="ManagedAction"/>. If this value is not specified
        /// <see cref="Compiler"/> will set it to the caller of its <c>Build</c> method.</para>
        /// </summary>
        static public string ClientAssembly
        {
            get { return clientAssembly; }
            set
            {
                //Debug.Assert(false);
                clientAssembly = value;

                var isCSScriptExecution = Environment.GetEnvironmentVariable("CSScriptRuntime") != null;

                if (isCSScriptExecution && clientAssembly.EndsWith("cscs.exe", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in AppDomain.CurrentDomain.GetAssemblies())
                        try
                        {
                            var attr = (System.Reflection.AssemblyDescriptionAttribute)item.GetCustomAttributes(typeof(System.Reflection.AssemblyDescriptionAttribute), false).FirstOrDefault();
                            if (attr != null && IO.File.Exists(attr.Description))
                            {
                                clientAssembly = item.Location;
                                break;
                            }
                        }
                        catch { }
                }
            }
        }

        /// <summary>
        /// Flag indicating whether to include PDB file of the assembly implementing ManagedCustomAction into MSI.  Default value is <c>False</c>.
        /// <para>If set to <c>false</c> PDB file will not be included and debugging of such CustomAction will not be possible.</para>
        /// </summary>
        static public bool IgnoreClientAssemblyPDB = false;

        internal static string ResolveClientAsm(string asmName, string outDir)
        {
            //restore the original assembly name as MakeSfxCA does not like renamed assemblies
            string newName = Utils.OriginalAssemblyFile(ClientAssembly);
            newName = newName.PathChangeDirectory(outDir);

            if (!ClientAssembly.SamePathAs(newName))
            {
                IO.File.Copy(ClientAssembly, newName, true);
                Compiler.TempFiles.Add(newName);

                var clientPdb = IO.Path.ChangeExtension(ClientAssembly, ".pdb");

                if (IO.File.Exists(clientPdb))
                {
                    IO.File.Copy(clientPdb, IO.Path.ChangeExtension(newName, ".pdb"), true);
                    Compiler.TempFiles.Add(IO.Path.ChangeExtension(newName, ".pdb"));
                }
            }
            return newName;
        }

        static void PackageManagedAsm(string asm, string nativeDll, string[] refAssemblies, string outDir, string configFilePath, Platform? platform = null)
        {
            string platformDir = "x86";
            if (platform.HasValue && platform.Value == Platform.x64)
                platformDir = "x64";

            var makeSfxCA = Utils.PathCombine(WixSdkLocation, @"MakeSfxCA.exe");
            var sfxcaDll = Utils.PathCombine(WixSdkLocation, platformDir + "\\sfxca.dll");

            outDir = IO.Path.GetFullPath(outDir);

            var outDll = IO.Path.GetFullPath(nativeDll);
            var asmFile = IO.Path.GetFullPath(asm);

            if (asmFile.EndsWith("%this%"))
                asmFile = ResolveClientAsm(asmFile, outDir);

            var requiredAsms = new List<string>(refAssemblies);

            //if WixSharp was "linked" with the client assembly not as script file but as external assembly
            string wixSharpAsm = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!ClientAssembly.SamePathAs(wixSharpAsm) && !asmFile.SamePathAs(wixSharpAsm))
            {
                if (!requiredAsms.Contains(wixSharpAsm))
                    requiredAsms.Add(wixSharpAsm);
            }

            string tempDir = IO.Path.GetTempFileName();

            var referencedAssemblies = "";
            foreach (string file in requiredAsms)
            {
                string refAasmFile;

                if (file == "%this%")
                {
                    refAasmFile = ResolveClientAsm(file, outDir);
                }
                else
                {
                    refAasmFile = Utils.OriginalAssemblyFile(file);

                    if (!file.SamePathAs(refAasmFile))
                    {
                        refAasmFile = refAasmFile.PathChangeDirectory(outDir);
                        IO.File.Copy(file, refAasmFile, true);
                        Compiler.TempFiles.Add(refAasmFile);
                    }
                }

                referencedAssemblies += "\"" + IO.Path.GetFullPath(refAasmFile) + "\" ";
            }

            var configFile = IO.Path.GetFullPath("CustomAction.config");

            if (configFilePath.IsNotEmpty())
            {
                configFile = configFilePath;
            }
            else
            {
                using (var writer = new IO.StreamWriter(configFile))
                    writer.Write(@"<?xml version=""1.0"" encoding=""utf-8"" ?>
                                                <configuration>
                                                    <startup useLegacyV2RuntimeActivationPolicy=""true"">

                                                        <supportedRuntime version=""v" + Environment.Version.ToNoRevisionString() + @"""/>

                                                        <supportedRuntime version=""v4.0"" sku="".NETFramework,Version=v4.0""/>
                                                        <supportedRuntime version=""v2.0.50727""/>
                                                        <supportedRuntime version=""v2.0.50215""/>
                                                        <supportedRuntime version=""v1.1.4322""/>
                                                    </startup>
                                                </configuration>");
            }
            Compiler.TempFiles.Add(configFile);

            string pdbFileArgument = null;
            if (!IgnoreClientAssemblyPDB && IO.File.Exists(IO.Path.ChangeExtension(asmFile, ".pdb")))
                pdbFileArgument = "\"" + IO.Path.ChangeExtension(asmFile, ".pdb") + "\" ";

            if (IO.File.Exists(outDll))
                IO.File.Delete(outDll);

            string makeSfxCA_args = "\"" + outDll + "\" " +
                        "\"" + sfxcaDll + "\" " +
                        "\"" + asmFile + "\" " +
                        "\"" + configFile + "\" " +
                        (pdbFileArgument ?? " ") +
                        referencedAssemblies +
                        "\"" + Utils.PathCombine(WixSdkLocation, "Microsoft.Deployment.WindowsInstaller.dll") + "\"";

            ProjectValidator.ValidateCAAssembly(asmFile);
#if DEBUG
            Console.WriteLine("<- Packing managed CA:");
            Console.WriteLine(makeSfxCA + " " + makeSfxCA_args);
            Console.WriteLine("->");
#endif
            Run(makeSfxCA, makeSfxCA_args);

            if (!IO.File.Exists(outDll))
                throw new ApplicationException("Cannot package ManagedCA assembly(" + asm + ")");

            Compiler.TempFiles.Add(outDll);
        }

        internal static Dictionary<string, string> EnvironmentFolders64Mapping = new Dictionary<string, string>
        {
            { "%ProgramFilesFolder%", "%ProgramFiles64Folder%" },
            { "%ProgramFiles%", "%ProgramFiles64%" },
            { "%CommonFilesFolder%", "%CommonFiles64Folder%" },
            { "%SystemFolder%", "%System64Folder%" },
            { "%CommonFiles%", "%CommonFiles64%" },
            { "%System%", "%System64%" },
        };

        internal static Dictionary<string, string> EnvironmentConstantsMapping = new Dictionary<string, string>
        {
            { "%AdminToolsFolder%", "AdminToolsFolder" },
            { "%AppDataFolder%", "AppDataFolder" },
            { "%CommonAppDataFolder%", "CommonAppDataFolder" },
            { "%CommonFiles64Folder%", "CommonFiles64Folder" },
            { "%CommonFilesFolder%", "CommonFilesFolder" },
            { "%DesktopFolder%", "DesktopFolder" },
            { "%FavoritesFolder%", "FavoritesFolder" },
            { "%FontsFolder%", "FontsFolder" },
            { "%LocalAppDataFolder%", "LocalAppDataFolder" },
            { "%MyPicturesFolder%", "MyPicturesFolder" },
            { "%PersonalFolder%", "PersonalFolder" },
            { "%ProgramFiles64Folder%", "ProgramFiles64Folder" },
            { "%ProgramFilesFolder%", "ProgramFilesFolder" },
            { "%ProgramMenuFolder%", "ProgramMenuFolder" },
            { "%SendToFolder%", "SendToFolder" },
            { "%StartMenuFolder%", "StartMenuFolder" },
            { "%StartupFolder%", "StartupFolder" },
            { "%System16Folder%", "System16Folder" },
            { "%System64Folder%", "System64Folder" },
            { "%SystemFolder%", "SystemFolder" },
            { "%TempFolder%", "TempFolder" },
            { "%TemplateFolder%", "TemplateFolder" },
            { "%WindowsFolder%", "WindowsFolder" },
            { "%WindowsVolume%", "WindowsVolume" },
            { "%AdminTools%", "AdminToolsFolder" },
            { "%AppData%", "AppDataFolder" },
            { "%CommonAppData%", "CommonAppDataFolder" },
            { "%CommonFiles64%", "CommonFiles64Folder" },
            { "%CommonFiles%", "CommonFilesFolder" },
            { "%Desktop%", "DesktopFolder" },
            { "%Favorites%", "FavoritesFolder" },
            { "%Fonts%", "FontsFolder" },
            { "%LocalAppData%", "LocalAppDataFolder" },
            { "%MyPictures%", "MyPicturesFolder" },
            { "%Personal%", "PersonalFolder" },
            { "%ProgramFiles64%", "ProgramFiles64Folder" },
            { "%ProgramFiles%", "ProgramFilesFolder" },
            { "%ProgramMenu%", "ProgramMenuFolder" },
            { "%SendTo%", "SendToFolder" },
            { "%StartMenu%", "StartMenuFolder" },
            { "%Startup%", "StartupFolder" },
            { "%System16%", "System16Folder" },
            { "%System64%", "System64Folder" },
            { "%System%", "SystemFolder" },
            { "%Temp%", "TempFolder" },
            { "%Template%", "TemplateFolder" },
            { "%Windows%", "WindowsFolder" }
        };



        static XElement AddDir(XElement parent, Dir wDir)
        {
            string name = wDir.Name;
            string id = "";

            if (wDir.IsIdSet())
            {
                id = wDir.Id;
            }
            else
            {
                //Special folder defined either directly or by Wix# environment constant
                //e.g. %ProgramFiles%, [ProgramFilesFolder] -> ProgramFilesFolder
                if (Compiler.EnvironmentConstantsMapping.ContainsKey(wDir.Name) ||                              // %ProgramFiles%
                    Compiler.EnvironmentConstantsMapping.ContainsValue(wDir.Name) ||                            // ProgramFilesFolder
                    Compiler.EnvironmentConstantsMapping.ContainsValue(wDir.Name.TrimStart('[').TrimEnd(']')))  // [ProgramFilesFolder]
                {
                    id = wDir.Name.Expand();
                    name = wDir.Name.Expand(); //name needs to be escaped
                }
                else
                {
                    id = parent.Attribute("Id").Value + "." + wDir.Name.Expand();
                }
            }

            XElement newSubDir = parent.AddElement(
                                             new XElement("Directory",
                                                 new XAttribute("Id", id),
                                                 new XAttribute("Name", name)));

            return newSubDir;
        }

        internal static void Run(string file, string args)
        {
            Trace.WriteLine("\"" + file + "\" " + args);

            Process p = new Process();
            p.StartInfo.FileName = file;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();

            string line = null;
            while (null != (line = p.StandardOutput.ReadLine()))
            {
                Console.WriteLine(line);
                Trace.WriteLine(line);
            }
            p.WaitForExit();
        }

        static string BuildSuppressWarningsArgs(int[] IDs)
        {
            var sb = new StringBuilder();
            foreach (int id in IDs)
                sb.Append("-sw{0} ".FormatInline(id));
            return sb.ToString();
        }

        /// <summary>
        /// Represents a wildcard running on the
        /// <see cref="T:System.Text.RegularExpressions"/> engine.
        /// </summary>
        /// <remarks>
        /// This class was developed and described by <c>reinux</c> in "Converting Wildcards to Regexes"
        /// on CodeProject (<c>http://www.codeproject.com/KB/recipes/wildcardtoregex.aspx</c>).
        /// </remarks>
        public class Wildcard : Regex
        {
            /// <summary>
            /// Initializes a wildcard with the given search pattern.
            /// </summary>
            /// <param name="pattern">The wildcard pattern to match.</param>
            public Wildcard(string pattern)
                : base(WildcardToRegex(pattern))
            {
            }

            /// <summary>
            /// Initializes a wildcard with the given search pattern and options.
            /// </summary>
            /// <param name="pattern">The wildcard pattern to match.</param>
            /// <param name="options">A combination of one or more
            /// <see cref="T:System.Text.RegexOptions"/>.</param>
            public Wildcard(string pattern, RegexOptions options)
                : base(WildcardToRegex(pattern), options)
            {
            }

            /// <summary>
            /// Converts a wildcard to a regex.
            /// </summary>
            /// <param name="pattern">The wildcard pattern to convert.</param>
            /// <returns>A regex equivalent of the given wildcard.</returns>
            public static string WildcardToRegex(string pattern)
            {
                return "^" + Regex.Escape(pattern).
                 Replace("\\*", ".*").
                 Replace("\\?", ".") + "$";
            }
        }

        static string GetShortcutWorkingDirectopry(string targetPath)
        {
            string workingDir = targetPath;
            var pos = workingDir.LastIndexOfAny(@"\/]".ToCharArray());
            if (pos != -1)
                workingDir = workingDir.Substring(0, pos + 1)
                                       .Replace("[", "")
                                       .Replace("]", "");
            return workingDir;
        }

        static string ResolveExtensionFile(string file)
        {
            string path = file;

            if (string.Compare(IO.Path.GetExtension(path), ".dll", true) != 0)
                path = path + ".dll";

            if (IO.File.Exists(path))
                return IO.Path.GetFullPath(path);

            if (!IO.Path.IsPathRooted(path))
            {
                path = IO.Path.Combine(WixLocation, path);

                if (IO.File.Exists(path))
                    return path;
            }

            return file;
        }
    }
}
