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
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using WixSharp.CommonTasks;

namespace WixSharp
{
    // Wix/Msi bug/limitation: every component that is to be placed in the user profile has to have Registry key
    // Wix# places dummy key into every component to handle the problem
    // Wix# auto-generates components contain RemoveFolder elements for all subfolders in the path chain. 
    // All auto-generates components are automatically inserted in all features

    /// <summary>
    /// Represents Wix# project. This class defines the WiX/MSI entities and their relationships.
    /// <para>
    /// 		<see cref="Project"/> instance can be compiled into complete MSI or WiX source file with one of the <see cref="Compiler"/> "Build" methods.
    /// </para>
    /// 	<para>
    /// Use <see cref="Project"/> non-default constructor or C# initializers to specify required installation components.
    /// </para>
    /// </summary>
    /// <example>
    /// 	<code>
    /// var project = new Project("MyProduct",
    ///                           new Dir(@"%ProgramFiles%\My Company\My Product",
    ///                                     new File(@"Files\Bin\MyApp.exe"),
    ///                                     new Dir(@"Docs\Manual",
    ///                                     new File(@"Files\Docs\Manual.txt"))));
    ///                                     
    /// project.GUID = new Guid("6f330b47-2577-43ad-9095-1861ba25889b");
    /// project.BuildMsi();
    /// </code>
    /// </example>
    public partial class Project : WixProject 
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Project"/> class.
        /// </summary>
        public Project() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="Project"/> class.
        /// </summary>
        /// <param name="name">The name of the project. Typically it is the name of the product to be installed.</param>
        /// <param name="items">The project installable items (e.g. directories, files, registry keys, Custom Actions).</param>
        public Project(string name, params WixObject[] items)
        {
            Name = name;
            OutFileName = name;

            var dirs = new List<Dir>();
            var actions = new List<Action>();
            var regs = new List<RegValue>();
            var envvars = new List<EnvironmentVariable>();
            var props = new List<Property>();
            var bins = new List<Binary>();
            var users = new List<User>();
            var sqls = new List<SqlDatabase>();
            var certs = new List<Certificate>();

            //var collections = items.Where(x=>x is WixItems).Sel

            foreach (WixObject obj in items)
            {
                var rawItems = new List<WixObject>();
                if (obj is WixItems)
                    rawItems.AddRange((obj as WixItems).Items);
                else
                    rawItems.Add(obj);

                foreach (WixObject item in rawItems)
                {
                    if (item is LaunchCondition)
                        LaunchConditions.Add(item as LaunchCondition);
                    else if (item is Dir)
                        dirs.Add(item as Dir);
                    else if (item is Action)
                        actions.Add(item as Action);
                    else if (item is RegValue)
                        regs.Add(item as RegValue);
                    else if (item is EnvironmentVariable)
                        envvars.Add(item as EnvironmentVariable);
                    else if (item is RegFile)
                    {
                        var file = item as RegFile;
                        var values = Tasks.ImportRegFile(file.Path);
                        if (file.Feature != null)
                            values.ForEach(x => x.Feature = file.Feature);
                        regs.AddRange(values);
                    }
                    else if (item is Property || item is PropertyRef)
                        props.Add(item as Property);
                    else if (item is Binary)
                        bins.Add(item as Binary);
                    else if (item is WixGuid)
                        GUID = (item as WixGuid).Value;
                    else if (item is User)
                        users.Add(item as User);
                    else if (item is SqlDatabase)
                        sqls.Add(item as SqlDatabase);
                    else if (item is Certificate)
                        certs.Add(item as Certificate);
                    else
                        throw new Exception("Unexpected object type is among Project constructor arguments: " + item.GetType().Name);
                }
            }

            Dirs = dirs.ToArray();
            Actions = actions.ToArray();
            RegValues = regs.ToArray();
            Properties = props.ToArray();
            Binaries = bins.ToArray();
            EnvironmentVariables = envvars.ToArray();
            Users = users.ToArray();
            SqlDatabases = sqls.ToArray();
            Certificates = certs.ToArray();
        }

        /// <summary>
        /// Product manufacturer name
        /// </summary>
        [Obsolete("Please use WixProject.ControlPanel instead (see ProductInfo sample for details).", true)]
        public string Manufacturer = Environment.UserName;

        /// <summary>
        /// The product full name or description.
        /// </summary>
        public string Description = "";

        /// <summary>
        /// Optional comments for browsing.
        /// </summary>
        [Obsolete("Please use WixProject.ControlPanel instead (see ProductInfo sample for details).", true)]
        public string Comments = "";

        internal virtual void Preprocess()
        {
            var managedActions = this.Actions.OfType<ManagedAction>()
                                             .Select(x => new { Action = x, Asm = x.ActionAssembly })
                                             .GroupBy(x => x.Asm)
                                             .ToDictionary(x => x.Key);

            foreach (var uniqueAsm in managedActions.Keys)
            {
                var actions = managedActions[uniqueAsm].Select(x => x.Action).ToArray();
                var refAsms = actions.SelectMany(a => a.RefAssemblies).Distinct().ToArray();
                actions.ForEach(a => a.RefAssemblies = refAsms);
            }

            if (WixSharp.Compiler.AutoGeneration.Map64InstallDirs && this.Platform.HasValue && this.Platform.Value == WixSharp.Platform.x64)
            {
                foreach (var dir in this.AllDirs)
                {
                    dir.Name = dir.Name.Map64Dirs();
                    foreach (Shortcut s in dir.Shortcuts)
                        s.Location = s.Location.Map64Dirs();
                }

                foreach (Shortcut s in this.AllFiles.SelectMany(f => f.Shortcuts))
                {
                    s.Location = s.Location.Map64Dirs();
                };
                
                foreach (var action in this.Actions.OfType<PathFileAction>())
                {
                    action.AppPath = action.AppPath.Map64Dirs();
                };
            }
        }

        /// <summary>
        /// Generic <see cref="T:WixSharp.WixEntity"/> container for defining WiX <c>Package</c> element attributes.
        /// <para>These attributes are the properties about the package to be placed in the Summary Information Stream. These are visible from COM through the IStream interface, and these properties can be seen on the package in Explorer. </para>
        ///<example>The following is an example of defining the <c>Package</c> attributes.
        ///<code>
        /// var project = 
        ///     new Project("My Product",
        ///         new Dir(@"%ProgramFiles%\My Company\My Product",
        ///         
        ///     ...
        ///         
        /// project.Package.AttributesDefinition = @"AdminImage=Yes;
        ///                                          Comments=Release Candidate;
        ///                                          Description=Fantastic product...";
        ///                                         
        /// Compiler.BuildMsi(project);
        /// </code>
        /// </example>
        /// </summary>
        public Package Package = new Package();
        /// <summary>
        /// The target platform type.
        /// </summary>
        public Platform? Platform;

        /// <summary>
        /// Generic <see cref="T:WixSharp.WixEntity"/> container for defining WiX <c>Media</c> element attributes.
        /// <para>These attributes describe a disk that makes up the source media for the installation.</para>
        ///<example>The following is an example of defining the <c>Package</c> attributes.
        ///<code>
        /// var project = 
        ///     new Project("My Product",
        ///         new Dir(@"%ProgramFiles%\My Company\My Product",
        ///         
        ///     ...
        ///         
        /// project.Media.AttributesDefinition = @"Id=2;
        ///                                        CompressionLevel=mszip";
        ///                                         
        /// Compiler.BuildMsi(project);
        /// </code>
        /// </example>
        /// </summary>
        public Media Media = new Media();
        /// <summary>
        /// Relative path to RTF file with the custom licence agreement to be displayed in the Licence dialog. 
        /// If this value is not specified the default WiX licence agreement will be used.
        /// </summary>
        public string LicenceFile = "";
        /// <summary>
        /// Name of the MSI file (without extension) to be build.
        /// </summary>
        [Obsolete("MSIFileName is a deprecated. Use OutFileName instead.", true)]
        public string MSIFileName
        {
            get
            {
                return OutFileName;
            }
            set
            {
                OutFileName = value;
            }
        }

        /// <summary>
        /// Path to the file containing the icon for AddRemovePrograms Control panel applet.
        /// </summary>
        [Obsolete("Please use WixProject.ControlPanel instead (see ProductInfo sample for details).", true)]
        public string AddRemoveProgramsIcon = "";
        /// <summary>
        /// The Encoding to be used for MSI UI dialogs. If not specified the 
        /// <c>System.Text.Encoding.Default</c> will be used.
        /// </summary>
        public Encoding Encoding = Encoding.Default;
        /// <summary>
        /// Type of the MSI User Interface. This value is assigned to the <c>UIRef</c> WiX element during the compilation.
        /// If specified <see cref="WUI.WixUI_Minimal"/> will used.
        /// </summary>
        public WUI UI = WUI.WixUI_Minimal;

        /// <summary>
        /// The Binary (assembly) implemening WiX embedded UI
        /// </summary>
        public Binary EmbeddedUI = null;

        /// <summary>
        /// The custom UI definition. Use CustomUIBuilder to generate the WiX UI definition or compose 
        /// <see cref="WixSharp.CustomUI"/> manually.
        /// </summary>
        public CustomUI CustomUI = null;

        /// <summary>
        /// This is the value of the <c>UpgradeCode</c> attribute of the Wix <c>Product</c> element. 
        /// <para>Both WiX and MSI consider this element as optional even it is the only available identifier 
        /// for defining relationship between different versions of the same product. Wix# in contrary enforces
        /// that value to allow any future updates of the product being installed.
        /// </para>
        /// <para> 
        /// If user doesn't specify this value Wix# engine will use <see cref="Project.GUID"/> as <c>UpgradeCode</c>.
        /// </para>
        /// </summary>
        public Guid? UpgradeCode;

        Guid? guid;

        /// <summary>
        /// This value uniquely identifies the software product being installed. 
        /// <para>
        /// All setup build scripts for different versions of the same product should have the same <see cref="GUID"/>.
        /// If user doesn't specify this value Wix# engine will generate new random GUID for it.
        /// </para>
        /// <remarks>This value should not be confused with MSI <c>Product.Id</c>, which is in fact 
        /// not an identifier of the product but rather an identifier of the product particular version. 
        /// MSI uses <c>UpgradeCode</c> as a common identification of the product regardless of it's version. 
        /// <para>In a way <see cref="GUID"/> is an alias for <see cref="UpgradeCode"/>.</para>
        /// </remarks>
        /// </summary>
        public Guid? GUID
        {
            get { return guid; }
            set
            {
                guid = value;
                WixGuid.ConsistentGenerationStartValue = new WixGuid.SequentialGuid(guid.Value);
            }
        }

        /// <summary>
        /// Path to the product icon file.
        /// </summary>
        [Obsolete("Please use WixProject.ControlPanel instead (see ProductInfo sample for details).", true)]
        public string ProductIcon = null;

        /// <summary>
        ///  Set of values in 'Add/Remove Programs' of Control Panel.
        /// </summary>
        public ProductInfo ControlPanelInfo = new ProductInfo();

        /// <summary>
        /// Use this attribute if you need to specify the installation scope of this package: per-machine or per-user.
        /// </summary>
        public InstallScope? InstallScope;

        /// <summary>
        /// Version of the product to be installed.
        /// </summary>
        public Version Version = new Version("1.0.0.0");

        /// <summary>
        /// Defines Major Upgrade behavior. By default it is <c>null</c> thus upgrade is not supported.
        /// <para>If you need to implement Major Upgrade define this member to appropriate 
        /// <see cref="MajorUpgradeStrategy"/> instance.</para>
        /// </summary>
        ///<example>The following is an example of building product MSI with auto uninstalling any older version of the product 
        ///and preventing downgrading.
        ///<code>
        /// var project = new Project("My Product",
        ///                   ...
        ///                   
        /// project.MajorUpgradeStrategy =  new MajorUpgradeStrategy
        ///                                 {
        ///                                     UpgradeVersions = VersionRange.OlderThanThis,
        ///                                     PreventDowngradingVersions = VersionRange.NewerThanThis,
        ///                                     NewerProductInstalledErrorMessage = "Newer version already installed",
        ///                                 };
        /// Compiler.BuildMsi(project);
        /// </code>
        /// or the same scenario but using predefined <c>MajorUpgradeStrategy.Default</c> strategy.
        ///<code>
        /// project.MajorUpgradeStrategy = MajorUpgradeStrategy.Default;                    
        /// </code>
        /// You can also specify custom range of versions:
        ///<code>
        /// project.MajorUpgradeStrategy =  new MajorUpgradeStrategy
        ///                                 {
        ///                                     UpgradeVersions = new VersionRange 
        ///                                                           { 
        ///                                                              Minimum = "2.0.5.0", IncludeMaximum = true,
        ///                                                              Maximum = "%this%", IncludeMaximum = false
        ///                                                           },
        ///                                     PreventDowngradingVersions = new VersionRange 
        ///                                                           { 
        ///                                                              Minimum = "%this%", IncludeMinimum = false
        ///                                                           },
        ///                                     NewerProductInstalledErrorMessage = "Newer version already installed",
        ///                                 };
        /// </code>
        /// Note that %this% will be replaced by Wix# compiler with <c>project.Version.ToString()</c> during the MSI building.
        /// However you can use explicit values (e.g. 1.0.0) if you prefer.
        /// </example>
        public MajorUpgradeStrategy MajorUpgradeStrategy;

        /// <summary>
        /// Generates all missing product Guids (e.g. <see cref="UpgradeCode"/> and <see cref="ProductId"/>).
        /// <para>Wix# compiler call this method just before building the MSI. However you can call it any time 
        /// if you want to preview auto-generated Guids.</para>
        /// </summary>
        public void GenerateProductGuids()
        {
            if (!GUID.HasValue)
                GUID = Guid.NewGuid();

            if (!UpgradeCode.HasValue)
                UpgradeCode = GUID;

            if (!ProductId.HasValue)
                ProductId = CalculateProductId(guid.Value, Version);
        }

        /// <summary>
        /// Calculates the product id.
        /// </summary>
        /// <param name="productGuid">The product GUID.</param>
        /// <param name="version">The version.</param>
        /// <returns></returns>
        public static Guid CalculateProductId(Guid productGuid, Version version)
        {
            return WixGuid.HashGuidByInteger(productGuid, version.GetHashCode() + 1);
        }
        /// <summary>
        /// This is the value of the <c>Id</c> attribute of the Wix <c>Product</c> element. 
        /// This value is unique for any given version of a product being installed.
        /// <para></para>
        /// If user doesn't specify this value Wix# engine will derive it from
        /// project <see cref="Project.GUID"/> and the product <see cref="Project.Version"/>.
        /// </summary>
        public Guid? ProductId;
        /// <summary>
        /// Collection of <see cref="Dir"/>s to be installed.
        /// </summary>
        public Dir[] Dirs = new Dir[0];
        /// <summary>
        /// Collection of <see cref="Actions"/>s to be performed during the installation.
        /// </summary>
        public Action[] Actions = new Action[0];
        /// <summary>
        /// Collection of <see cref="RegValue"/>s to be set during the installation.
        /// </summary>
        public RegValue[] RegValues = new RegValue[0];
        /// <summary>
        /// Collection of <see cref="EnvironmentVariable"/>s to be set during the installation.
        /// </summary>
        public EnvironmentVariable[] EnvironmentVariables = new EnvironmentVariable[0];
        /// <summary>
        /// Collection of <see cref="Certificate"/> to be installed.
        /// </summary>
        public Certificate[] Certificates = new Certificate[0];
        /// <summary>
        /// Collection of WiX/MSI <see cref="Property"/> objects to be created during the installed.
        /// </summary>
        public Property[] Properties = new Property[0];
        /// <summary>
        /// Indicates whether compiler should emit consistent package Id (package code). Set <c>EmitConsistentPackageId</c> to 'false' (default value) if 
        /// you want the WiX compilers automatically generate a new package code for each new .msi file built. Or set it to 'true' if you want Wix# to auto generate a 
        /// unique consistent package code for a given combination of the product code, product version and product upgrade code. 
        /// <para>
        /// WiX package code generation policy discourages the use of this attribute as it is a primary MSI identifier 
        /// used to distinguish packages in ARP. Thus WiX tools always auto-generate the code for each build. This in turn makes it impossible to
        /// rebuild a truly identical MSIs from the same WiX code even with the same set of product code, version and upgrade code. 
        /// </para><para>
        /// This very artificial limitation has severe practical impact. For example if a specific MSI file is lost it cannot be recreated even if 
        /// the original source code that was used to built the lost MSI is available.
        /// </para><para>
        /// From another hand Wix# encourages using a singe GUID (Project.GUID) as a primary identifier of the product. Thus all other MSI identifiers 
        /// can be derived by the compiler from the unique combination of this GUID and the product version. This also included generation of the package Id
        /// attribute controlled by the EmitConsistentPackageId.
        /// </para><para>
        /// Wix# does not changes the WiX default package code generation it just gives the opportunity to control it when required. 
        /// </para>
        /// </summary>
        public bool EmitConsistentPackageId = false;
        /// <summary>
        /// Collection of WiX/MSI <see cref="Binary"/> objects to be embedded into MSI database. 
        /// Normally you doe not need to deal with this property as <see cref="Compiler"/> will populate
        /// it automatically.
        /// </summary>
        public Binary[] Binaries = new Binary[0];
        /// <summary>
        /// Collection of paths to the assemblies referenced by <see cref="ManagedAction"/>s.
        /// </summary>
        public List<string> DefaultRefAssemblies = new List<string>();
        /// <summary>
        /// Collection of the <see cref="T:WixSharp.LaunchCondition"/>s associated with the setup.
        /// </summary>
        public List<LaunchCondition> LaunchConditions = new List<LaunchCondition>();

        /// <summary>
        /// Collection of Wix:UtilExtension User objects representings users 'for all kinds of things'
        /// </summary>
        public User[] Users = new User[0];

        /// <summary>
        /// Collection of WiX:SqlExtension SqlDatabase objects representing databases to be created,
        /// modifed, or interacted with during MSI execution.
        /// </summary>
        public SqlDatabase[] SqlDatabases = new SqlDatabase[0];

        /// <summary>
        /// Path to the file containing the image (e.g. bmp) setup dialogs banner. If not specified default image will be used.
        /// </summary>
        public string BannerImage = "";

        /// <summary>
        /// Path to the file containing the image (e.g. bmp) setup dialogs background. If not specified default image will be used.
        /// </summary>
        public string BackgroundImage = "";

        private Feature _defaultFeature = new Feature("Complete");
        /// <summary>
        /// Gets or Sets the default Feature for the project. 
        /// All elements without an explicitly assigned Feature will be placed under the DefaultFeature.
        /// If DefaultFeature is not set, then DefaultFeature will default to a Feature with name 'Complete'.
        /// </summary>
        public Feature DefaultFeature
        {
            get { return _defaultFeature; }
            set
            {
                if (value == null) throw new ArgumentNullException("value", "DefaultFeature cannot be null");
                _defaultFeature = value;
            }
        }

        /// <summary>
        /// Resolves all wild card specifications if any.
        /// <para>
        /// This method is called by <see cref="Compiler" /> during the compilation. However it might be convenient
        /// to call it before the compilation if any files matching the wild card mask need to be handled in special
        /// way (e.g. shortcuts created). See <c>WildCard Files</c> example.
        /// </para><remarks><see cref="ResolveWildCards" /> should be called only after <see cref="T:WixSharp.WixProject.SourceBaseDir" /> is set.
        /// Otherwise wild card paths may not be resolved correctly.</remarks>
        /// </summary>
        /// <param name="ignoreEmptyDirectories">if set to <c>true</c> empty directories are ignored and not added to the project.</param>
        /// <returns></returns>
        public Project ResolveWildCards(bool ignoreEmptyDirectories = false)
        {
            int iterator = 0;
            var dirList = new List<Dir>();
            var fileList = new List<File>();

            dirList.AddRange(Dirs);

            while (iterator < dirList.Count)
            {
                foreach (Files dirItems in dirList[iterator].FileCollections)
                    foreach (WixEntity item in dirItems.GetAllItems(SourceBaseDir))
                        if (item is DirFiles)
                            dirList[iterator].DirFileCollections = dirList[iterator].DirFileCollections.Add(item as DirFiles);
                        else if (item is Dir)
                            dirList[iterator].Dirs = dirList[iterator].Dirs.Add(item as Dir);

                foreach (Dir dir in dirList[iterator].Dirs)
                    dirList.Add(dir);

                foreach (DirFiles coll in dirList[iterator].DirFileCollections)
                    dirList[iterator].Files = dirList[iterator].Files.Combine<File>(coll.GetFiles(SourceBaseDir));

                //clear resolved collections
                dirList[iterator].FileCollections = new Files[0];
                dirList[iterator].DirFileCollections = new DirFiles[0];

                iterator++;
            }

            if (ignoreEmptyDirectories)
            {
                var emptyDirs = AllDirs.Where(d => !d.Files.Any() && !d.Dirs.Any());

                emptyDirs.ForEach(emptyDir => AllDirs.ForEach(d =>
                                              {
                                                  if (d.Dirs.Contains(emptyDir))
                                                      d.Dirs = d.Dirs.Where(x => x != emptyDir).ToArray();
                                              }));
            }

            return this;
        }
        /// <summary>
        /// Returns all <see cref="File"/>s of the <see cref="Project"/> matching the <paramref name="match"/> pattern.
        /// </summary>
        /// <param name="match">The match pattern.</param>
        /// <returns>Matching <see cref="File"/>s.</returns>
        public File[] FindFile(Predicate<File> match)
        {
            return (from f in AllFiles
                    where match(f)
                    select f)
                    .ToArray();
        }
        /// <summary>
        /// Flattened "view" of all <see cref="File"/>s of the <see cref="Project"/>.
        /// </summary>
        public File[] AllFiles
        {
            get
            {
                int iterator = 0;
                var dirList = new List<Dir>();
                var fileList = new List<File>();

                dirList.AddRange(Dirs);

                while (iterator < dirList.Count)
                {
                    foreach (Dir dir in dirList[iterator].Dirs)
                        dirList.Add(dir);

                    fileList.AddRange(dirList[iterator].Files);

                    iterator++;
                }

                return fileList.ToArray();
            }
        }

        /// <summary>
        /// Flattened "view" of all <see cref="Dir"/>s of the <see cref="Project"/>.
        /// </summary>
        public Dir[] AllDirs
        {
            get
            {
                int iterator = 0;
                var dirList = new List<Dir>();

                dirList.AddRange(Dirs);

                while (iterator < dirList.Count)
                {
                    dirList.AddRange(dirList[iterator].Dirs);
                    iterator++;
                }

                return dirList.ToArray();
            }
        }


        /// <summary>
        /// Finds <see cref="T:WixSharp.Dir"/> corresponding to the specified path.
        /// <example>
        /// <code>
        /// new Project("MyProduct",
        ///     new Dir("%ProgramFiles%",
        ///         new Dir("My Company",
        ///             new Dir("My Product", 
        ///             ...
        /// </code>
        /// In the sample above the call <c>FindDir(@"%ProgramFiles%\My Company\My Product")</c> returns the last declared <see cref="T:WixSharp.Dir"/>.
        /// </example>
        /// </summary>
        /// <param name="path">The path string.</param>
        /// <returns><see cref="T:WixSharp.Dir"/> instance if the search was succesfull, otherwise return <c>null</c></returns>
        public Dir FindDir(string path)
        {
            int iterator = 0;
            var dirList = new List<Dir>();
            int tokenIndex = 0;
            string[] pathTokens = path.Split("\\/".ToCharArray());

            dirList.AddRange(Dirs);

            while (iterator < dirList.Count)
            {
                string dirName = dirList[iterator].Name.Expand().ToLower();
                string currentSubDir = pathTokens[tokenIndex].Expand().ToLower();
                if (dirName == currentSubDir)
                {
                    if (tokenIndex == pathTokens.Length - 1)
                        return dirList[iterator];

                    dirList.AddRange(dirList[iterator].Dirs);
                    tokenIndex++;
                }
                iterator++;
            }

            return null;
        }

        string codepage = "";
        /// <summary>
        /// Installation UI Codepage. If not specified 
        /// ANSICodePage of the <see cref="T:WixSharp.WixProject.Language"/> will be used.
        /// </summary>
        public string Codepage
        {
            get
            {
                if (!codepage.IsEmpty())
                    return codepage;
                else
                    return Encoding.GetEncoding(new CultureInfo(Language).TextInfo.ANSICodePage).WebName;
            }
            set
            {
                codepage = value;
            }
        }

        /// <summary>
        /// <see cref="CultureInfo"/> object based on the specified <see cref="T:WixSharp.WixProject.Language"/>
        /// </summary>
        public string Culture
        {
            get { return new CultureInfo(Language).Name; }
        }

        internal bool IsLocalized
        {
            get { return (Language.ToLower() != "en-us" && Language.ToLower() != "en") || !localizationFile.IsEmpty(); }
        }

        string localizationFile = "";
        /// <summary>
        /// Path to the Localization file. This value is used only if the setup language is not <c>"en-US"</c>.
        /// <para>If the <see cref="LocalizationFile"/> is not specified and the setup language 
        /// <see cref="Compiler"/> will generate <see cref="LocalizationFile"/> value as following:
        /// <c>WixUI_[Language].wxl</c>.
        /// </para>
        /// </summary>
        public string LocalizationFile
        {
            get { return localizationFile.IsEmpty() ? "wixui_" + new CultureInfo(this.Language).Name + ".wxl" : localizationFile; }
            set { localizationFile = value; }
        }

        /// <summary>
        /// Name (path) of the directory which was assigned <see cref="T:WixSharp.Compiler.AutoGeneration.InstallDirDefaultId"/> ID as part of XML auto-generation (see <see cref="T:WixSharp.AutoGenerationOptions"/>).
        /// </summary>
        public string AutoAssignedInstallDirPath = "";

        /// <summary>
        /// Builds the MSI file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="path">The path to the MSI file to be build.</param>
        /// <returns>Path to the built MSI file.</returns>
        public string BuildMsi(string path = null)
        {
            if (Compiler.ClientAssembly.IsEmpty())
                Compiler.ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            if (path == null)
                return Compiler.BuildMsi(this);
            else
                return Compiler.BuildMsi(this, path);
        }

        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSI with WiX toolset.
        /// </summary>
        /// <param name="path">The path to the batch file to be build.</param>
        /// <returns>Path to the batch file.</returns>
        public string BuildMsiCmd(string path = null)
        {
            if (Compiler.ClientAssembly.IsEmpty())
                Compiler.ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            if (path == null)
                return Compiler.BuildMsiCmd(this);
            else
                return Compiler.BuildMsiCmd(this, path);
        }

        /// <summary>
        /// Builds the WiX source file (*.wxs) from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="path">The path to the WXS file to be build.</param>
        /// <param name="type">The type (<see cref="Compiler.OutputType"/>) of the setup file to be defined in the source file (MSI vs. MSM).</param>
        /// <returns>Path to the built WXS file.</returns>
        public string BuildWxs(Compiler.OutputType type = Compiler.OutputType.MSI, string path = null)
        {
            if (Compiler.ClientAssembly.IsEmpty())
                Compiler.ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            if (path == null)
                return Compiler.BuildWxs(this, type);
            else
                return Compiler.BuildWxs(this, path, type);
        }

        /// <summary>
        /// Builds the MSM file from the specified <see cref="Project"/> instance.
        /// </summary>
        /// <param name="path">The path to the MSM file to be build.</param>
        /// <returns>Path to the built MSM file.</returns>
        public string BuildMsm(string path = null)
        {
            if (Compiler.ClientAssembly.IsEmpty())
                Compiler.ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            if (path == null)
                return Compiler.BuildMsm(this);
            else
                return Compiler.BuildMsm(this, path);
        }

        /// <summary>
        /// Builds the WiX source file and generates batch file capable of building
        /// MSM with WiX toolset.
        /// </summary>
        /// <param name="path">The path to the batch file to be build.</param>
        /// <returns>Path to the batch file.</returns>
        public string BuildMsmCmd(string path = null)
        {
            if (Compiler.ClientAssembly.IsEmpty())
                Compiler.ClientAssembly = System.Reflection.Assembly.GetCallingAssembly().Location;

            if (path == null)
                return Compiler.BuildMsmCmd(this);
            else
                return Compiler.BuildMsmCmd(this, path);
        }
    }
}
