using MBBSEmu.Database.Repositories.Account;
using MBBSEmu.Database.Repositories.AccountKey;
using MBBSEmu.DependencyInjection;
using MBBSEmu.HostProcess;
using MBBSEmu.IO;
using MBBSEmu.Module;
using MBBSEmu.Reports;
using MBBSEmu.Resources;
using MBBSEmu.Server;
using MBBSEmu.Server.Socket;
using MBBSEmu.Session;
using Microsoft.Extensions.Configuration;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using MBBSEmu.Session.Enums;

namespace MBBSEmu
{
    public class Program
    {
        public const string DefaultEmuSettingsFilename = "appsettings.json";

        private ILogger _logger;

        private string sInputModule = string.Empty;
        private string sInputPath = string.Empty;
        private bool bApiReport = false;
        private bool bConfigFile = false;
        private string sConfigFile = string.Empty;
        private string sSettingsFile;
        private bool bResetDatabase = false;
        private string sSysopPassword = string.Empty;
        private List<IStoppable> runningServices = new List<IStoppable>();
        private int cancellationRequests = 0;

        static void Main(string[] args)
        {
            new Program().Run(args);
        }

        private void Run(String[] args) {
            try
            {
                if (args.Length == 0)
                    args = new[] { "-?" };

                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToUpper())
                    {
                        case "-DBRESET":
                            {
                                bResetDatabase = true;
                                if (i + 1 < args.Length && args[i + 1][0] != '-')
                                {
                                    sSysopPassword = args[i + 1];
                                    i++;
                                }

                                break;
                            }
                        case "-APIREPORT":
                            bApiReport = true;
                            break;
                        case "-M":
                            sInputModule = args[i + 1];
                            i++;
                            break;
                        case "-P":
                            sInputPath = args[i + 1];
                            i++;
                            break;
                        case "-?":
                            Console.WriteLine(new ResourceManager().GetString("MBBSEmu.Assets.commandLineHelp.txt"));
                            Console.WriteLine($"Version: {new ResourceManager().GetString("MBBSEmu.Assets.version.txt")}");
                            return;
                        case "-CONFIG":
                        case "-C":
                            {
                                bConfigFile = true;
                                //Is there a following argument that doesn't start with '-'
                                //If so, it's the config file name
                                if (i + 1 < args.Length && args[i + 1][0] != '-')
                                {
                                    sConfigFile = args[i + 1];

                                    if (!File.Exists(sConfigFile))
                                    {
                                        Console.Write($"Specified Module Configuration File not found: {sConfigFile}");
                                        return;
                                    }
                                    i++;
                                }
                                else
                                {
                                    Console.WriteLine("Please specify a Module Configuration File when using the -C command line option");
                                }

                                break;
                            }
                        case "-S":
                            {
                                //Is there a following argument that doesn't start with '-'
                                //If so, it's the config file name
                                if (i + 1 < args.Length && args[i + 1][0] != '-')
                                {
                                    sSettingsFile =  args[i + 1];

                                    if (!File.Exists(sSettingsFile))
                                    {

                                        Console.WriteLine($"Specified MBBSEmu settings not found: {sSettingsFile}");
                                        return;
                                    }
                                    i++;
                                }
                                else
                                {
                                    Console.WriteLine("Please specify an MBBSEmu configuration file when using the -S command line option");
                                }

                                break;
                            }
                        default:
                            Console.WriteLine($"Unknown Command Line Argument: {args[i]}");
                            return;
                    }
                }

                ServiceResolver.Create(sSettingsFile ?? DefaultEmuSettingsFilename);

                _logger = ServiceResolver.GetService<ILogger>();
                var config = ServiceResolver.GetService<IConfiguration>();
                var fileUtility = ServiceResolver.GetService<IFileUtility>();

                //Database Reset
                if (bResetDatabase)
                    DatabaseReset();

                //Setup Generic Database
                if (!File.Exists($"BBSGEN.DAT"))
                {
                    _logger.Warn($"Unable to find MajorBBS/WG Generic User Database, creating new copy of BBSGEN.VIR to BBSGEN.DAT");

                    var resourceManager = ServiceResolver.GetService<IResourceManager>();

                    File.WriteAllBytes($"BBSGEN.DAT", resourceManager.GetResource("MBBSEmu.Assets.BBSGEN.VIR").ToArray());
                }

                //Setup Modules
                var modules = new List<MbbsModule>();
                if (!string.IsNullOrEmpty(sInputModule))
                {
                    //Load Command Line
                    modules.Add(new MbbsModule(fileUtility, sInputModule, sInputPath));
                }
                else if (bConfigFile)
                {
                    //Load Config File
                    var moduleConfiguration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile(sConfigFile, optional: false, reloadOnChange: true).Build();

                    foreach (var m in moduleConfiguration.GetSection("Modules").GetChildren())
                    {
                        _logger.Info($"Loading {m["Identifier"]}");
                        modules.Add(new MbbsModule(fileUtility, m["Identifier"], m["Path"]));
                    }
                }
                else
                {
                    _logger.Warn($"You must specify a module to load either via Command Line or Config File");
                    _logger.Warn($"View help documentation using -? for more information");
                    return;
                }

                //API Report
                if (bApiReport)
                {
                    foreach (var m in modules)
                    {
                        var apiReport = new ApiReport(m);
                        apiReport.GenerateReport();
                    }
                    return;
                }

                //Database Sanity Checks
                var databaseFile = ServiceResolver.GetService<IConfiguration>()["Database.File"];
                if (string.IsNullOrEmpty(databaseFile))
                {
                    _logger.Fatal($"Please set a valid database filename (eg: mbbsemu.db) in the appsettings.json file before running MBBSEmu");
                    return;
                }
                if (!File.Exists($"{databaseFile}"))
                {
                    _logger.Warn($"SQLite Database File {databaseFile} missing, performing Database Reset to perform initial configuration");
                    DatabaseReset();
                }

                //Setup and Run Host
                var host = ServiceResolver.GetService<IMbbsHost>();
                foreach (var m in modules)
                    host.AddModule(m);

                host.Start();

                runningServices.Add(host);

                //Setup and Run Telnet Server
                if (bool.TryParse(config["Telnet.Enabled"], out var telnetEnabled) && telnetEnabled)
                {
                    if (string.IsNullOrEmpty("Telnet.Port"))
                    {
                        _logger.Error("You must specify a port via Telnet.Port in appconfig.json if you're going to enable Telnet");
                        return;
                    }

                    var telnetService = ServiceResolver.GetService<ISocketServer>();
                    telnetService.Start(EnumSessionType.Telnet, int.Parse(config["Telnet.Port"]));

                    _logger.Info($"Telnet listening on port {config["Telnet.Port"]}");

                    runningServices.Add(telnetService);
                }
                else
                {
                    _logger.Info("Telnet Server Disabled (via appsettings.json)");
                }

                //Setup and Run Rlogin Server
                if (bool.TryParse(config["Rlogin.Enabled"], out var rloginEnabled) && rloginEnabled)
                {
                    if (string.IsNullOrEmpty("Rlogin.Port"))
                    {
                        _logger.Error("You must specify a port via Rlogin.Port in appconfig.json if you're going to enable Rlogin");
                        return;
                    }

                    if (string.IsNullOrEmpty("Rlogin.RemoteIP"))
                    {
                        _logger.Error("For security reasons, you must specify an authorized Remote IP via Rlogin.Port if you're going to enable Rlogin");
                        return;
                    }

                    var rloginService = ServiceResolver.GetService<ISocketServer>();
                    rloginService.Start(EnumSessionType.Rlogin, int.Parse(config["Rlogin.Port"]));

                    _logger.Info($"Rlogin listening on port {config["Rlogin.Port"]}");

                    runningServices.Add(rloginService);

                    if (bool.Parse(config["Rlogin.PortPerModule"]))
                    {
                        var rloginPort = int.Parse(config["Rlogin.Port"]) + 1;
                        foreach (var m in modules)
                        {
                            _logger.Info($"Rlogin {m.ModuleIdentifier} listening on port {rloginPort}");
                            rloginService = ServiceResolver.GetService<ISocketServer>();
                            rloginService.Start(EnumSessionType.Rlogin, rloginPort++, m.ModuleIdentifier);
                            runningServices.Add(rloginService);
                        }
                    }
                }
                else
                {
                    _logger.Info("Rlogin Server Disabled (via appsettings.json)");
                }

                _logger.Info($"Started MBBSEmu Build #{new ResourceManager().GetString("MBBSEmu.Assets.version.txt")}");

                Console.CancelKeyPress += cancelKeyPressHandler;
            }
            catch (Exception e)
            {
                Console.WriteLine("Critical Exception has occured:");
                Console.WriteLine(e);
                Environment.Exit(0);
            }
        }

        private void cancelKeyPressHandler(object sender, ConsoleCancelEventArgs args)
        {
            // so args.Cancel is a bit strange. Cancel means to cancel the Ctrl-C processing, so
            // setting it to true keeps the app alive. We want this at first to allow the shutdown
            // routines to process naturally. If we get a 2nd (or more) Ctrl-C, then we set
            // args.Cancel to false which means the app will die a horrible death, and prevents the
            // app from being unkillable by normal means.
            args.Cancel = cancellationRequests <= 0;

            cancellationRequests++;

            _logger.Warn("BBS Shutting down");

            foreach (var runningService in runningServices)
            {
                runningService.Stop();
            }
        }

        /// <summary>
        ///     Performs a Database Reset
        ///
        ///     Deletes the Accounts Table and sets up a new SYSOP and GUEST user
        /// </summary>
        private void DatabaseReset()
        {
            _logger.Info("Resetting Database...");
            var acct = ServiceResolver.GetService<IAccountRepository>();
            if (acct.TableExists())
                acct.DropTable();
            acct.CreateTable();

            if (string.IsNullOrEmpty(sSysopPassword))
            {
                var bPasswordMatch = false;
                while (!bPasswordMatch)
                {
                    Console.Write("Enter New Sysop Password: ");
                    var password1 = Console.ReadLine();
                    Console.Write("Re-Enter New Sysop Password: ");
                    var password2 = Console.ReadLine();
                    if (password1 == password2)
                    {
                        bPasswordMatch = true;
                        sSysopPassword = password1;
                    }
                    else
                    {
                        Console.WriteLine("Password mismatch, please tray again.");
                    }
                }
            }

            var sysopUserId = acct.InsertAccount("sysop", sSysopPassword, "sysop@mbbsemu.com");
            var guestUserId = acct.InsertAccount("guest", "guest", "guest@mbbsemu.com");

            var keys = ServiceResolver.GetService<IAccountKeyRepository>();

            if (keys.TableExists())
                keys.DropTable();

            keys.CreateTable();

            //Keys for SYSOP
            keys.InsertAccountKey(sysopUserId, "DEMO");
            keys.InsertAccountKey(sysopUserId, "NORMAL");
            keys.InsertAccountKey(sysopUserId, "SUPER");
            keys.InsertAccountKey(sysopUserId, "SYSOP");

            //Keys for GUEST
            keys.InsertAccountKey(guestUserId, "DEMO");
            keys.InsertAccountKey(guestUserId, "NORMAL");

            _logger.Info("Database Reset!");
        }
    }
}
