#region File Header

/********************************************************************
 * COPYRIGHT:
 *    This software program is furnished to the user under license
 *    by eSymmetrix, Inc, and use thereof is subject to applicable 
 *    U.S. and international law. This software program may not be 
 *    reproduced, transmitted, or disclosed to third parties, in 
 *    whole or in part, in any form or by any manner, electronic or
 *    mechanical, without the express written consent of eSymmetrix, Inc,
 *    except to the extent provided for by applicable license.
 *
 *    Copyright © 2008 by eSymmetrix, Inc.  All rights reserved.
 *******************************************************************/
using System;
using System.Configuration;
using System.Diagnostics;
using System.Windows.Forms;

#endregion File Header

namespace Gibraltar.Agent.Windows.Packager
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            Log.Initializing += OnLogInitializing;
            Log.StartSession("Packager Application Starting");

            try
            {
                ExitCodes returnVal = ExitCodes.Success;

                //now, there are two TOTALLY different ways we can go:  With or without a UI.  We need to parse the command line to find out.            
                Arguments commandArgs = null;
                try
                {
                    commandArgs = new Arguments(args);
                }
                catch (Exception ex)
                {
                    Log.RecordException(ex, "Startup", true);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                ConfigurationSection configuration = null;
                try
                {
                    //see if we can get a configuration section
                    configuration = ConfigurationManager.GetSection("gibraltar/packager") as ConfigurationSection;
                }
                catch (Exception ex)
                {
                    returnVal = ExitCodes.BadConfigurationFile;
                    Log.RecordException(ex, "Startup", true);
                }

                string productName = null;
                string applicationName = null;
                string folder = null;

                if (configuration != null)
                {
                    try
                    {
                        productName = configuration.GetType().GetProperty("ProductName").GetValue(configuration, null) as string;
                        applicationName = configuration.GetType().GetProperty("ApplicationName").GetValue(configuration, null) as string;
                    }
                    catch (Exception ex)
                    {
                        returnVal = ExitCodes.BadConfigurationFile;
                        Log.RecordException(ex, "Startup", true);
                    }
                }

                //see if we got a product or app name on the command line.  if specified they override our settings, even blanks.
                bool silentMode = false;
                bool waitForProcessExit = false;
                int monitorProcessId = 0;
                if (commandArgs != null)
                {
                    productName = commandArgs["p"] ?? productName;
                    applicationName = commandArgs["a"] ?? applicationName;
                    folder = commandArgs["folder"];

                    silentMode = (commandArgs["s"] != null);
                    waitForProcessExit = (commandArgs["w"] != null);
                    if (waitForProcessExit)
                    {
                        string rawProcessId = commandArgs["w"];
                        if (int.TryParse(rawProcessId, out monitorProcessId) == false)
                        {
                            Log.Error("Startup", "Unable to process command line", "The command line argument for Process ID '{0}' could not be interpreted as a number.", rawProcessId);
                        }
                    }
                }

                //if we couldn't find a product name, we're in trouble
                if (string.IsNullOrEmpty(productName))
                {
                    returnVal = ExitCodes.MissingProductName;
                    Log.Error("Startup", "Unable to Start due to Configuration", "There is no product name specified in the configuration so the packager can't start.");
                    if (silentMode)
                    {
                        Console.WriteLine("There is no product name specified so the packager can't start.");
                    }
                    else
                    {
                        MessageBox.Show("Diagnostic information can't be packaged because no product was configured.\r\nPlease configure a product name in the Packager.config file.",
                            "Unable to Package Information", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    }
                }
                else
                {
                    //see if we have to wait for a process to exit before we can run.
                    if (waitForProcessExit)
                    {
                        //try to get the process, it may no longer exist.
                        Process monitorProcess = null;
                        try
                        {
                            monitorProcess = Process.GetProcessById(monitorProcessId);
                        }
                        catch (ArgumentException ex)
                        {
                            Log.Verbose(ex, "Startup", "Unable to find the process to wait on", "When attempting to get the process object for the specified wait process Id '{0}' an exception was thrown:\r\n{1}", monitorProcessId, ex.Message);
                        }

                        bool hasExited = true;
                        if (monitorProcess != null)
                        {
                            Log.Information("Startup", "Waiting on calling process to exit", "The wait option was specified with Process ID {0}, so the packager will wait for it to exit before continuing (up to 60 seconds).", monitorProcessId);
                            hasExited = monitorProcess.WaitForExit(60000); //we don't want to wait forever, it'll cause a problem.
                        }

                        Log.Information("Startup", "Wait on process complete", hasExited ? "The process we were waiting on (pid {0}) is no longer running so the packager can continue."
                            : "The process we were waiting on (pid {0}) is still running but we've waited as long as we're willing to so we'll continue and package anyway.", monitorProcessId);
                    }

                    if (silentMode)
                    {
                        //no UI allowed!  use the packager directly, but we better have enough data.
                        string transmitMode = commandArgs["m"];
                        if (string.IsNullOrEmpty(transmitMode))
                        {
                            returnVal = ExitCodes.MissingTransmitMode;
                            Log.Error("Startup", "Unable to process command line", "There is no transmit mode (-m) specified so the packager can't start.");
                            Console.WriteLine("There is no transmit mode (-m) specified so the packager can't start.");
                        }
                        else
                        {
                            try
                            {
                                using (Agent.Packager packager = new Agent.Packager(productName, applicationName, folder))
                                {
                                    transmitMode = transmitMode.ToUpperInvariant();
                                    switch (transmitMode)
                                    {
                                        case "SERVER":
                                            //We need all of the server-specific parameters
                                            string sdsCustomer = commandArgs["customer"];
                                            string sdsServer = commandArgs["server"];
                                            int sdsPort = 0;
                                            bool sdsUseSsl = false;
                                            string sdsBaseDirectory = null;
                                            string sdsRepository = null;

                                            if (string.IsNullOrEmpty(sdsServer) == false)
                                            {
                                                int.TryParse(commandArgs["port"], out sdsPort);

                                                string ssl = commandArgs["ssl"];
                                                if (string.IsNullOrEmpty(ssl) == false)
                                                {
                                                    bool.TryParse(ssl, out sdsUseSsl);
                                                }

                                                sdsBaseDirectory = commandArgs["directory"];
                                                sdsRepository = commandArgs["repository"];
                                            }

                                            bool purgeSentSessions = false;
                                            string purgeRaw = commandArgs["purgeSentSessions"];
                                            if (string.IsNullOrEmpty(purgeRaw) == false)
                                            {
                                                bool.TryParse(purgeRaw, out purgeSentSessions);
                                            }

                                            //careful - we have to make different calls depending on whether we want to override server info.
                                            if (string.IsNullOrEmpty(sdsCustomer) == false)
                                            {
                                                //SDS using our server
                                                packager.SendToServer(SessionCriteria.NewSessions, true, purgeSentSessions, sdsCustomer);
                                            }
                                            else if (string.IsNullOrEmpty(sdsServer) == false)
                                            {
                                                //override the server and all its related options.
                                                packager.SendToServer(SessionCriteria.NewSessions, true, purgeSentSessions, sdsServer, sdsPort, sdsUseSsl, sdsBaseDirectory, sdsRepository);
                                            }
                                            else
                                            {
                                                returnVal = ExitCodes.MissingServerInfo;
                                                Log.Error("Startup", "Unable to process command line", "There is no customer or server specified but the transmit mode is set to server.");
                                                Console.WriteLine("There is no customer or server specified but the transmit mode is set to server.");
                                            }

                                            break;
                                        case "EMAIL":
                                            //We need all of the email-specific parameters
                                            string destinationAddress = commandArgs["d"];
                                            string fromAddress = commandArgs["f"];
                                            string emailServer = commandArgs["server"];
                                            int port = 0;
                                            string emailUser = null;
                                            string emailPassword = null;
                                            bool useSsl = false;

                                            if (string.IsNullOrEmpty(emailServer) == false)
                                            {
                                                int.TryParse(commandArgs["port"], out port);

                                                string ssl = commandArgs["ssl"];
                                                if (string.IsNullOrEmpty(ssl) == false)
                                                {
                                                    bool.TryParse(commandArgs["ssl"], out useSsl);
                                                }

                                                emailUser = commandArgs["user"];

                                                if (string.IsNullOrEmpty(emailUser) == false)
                                                {
                                                    emailPassword = commandArgs["password"];
                                                }
                                            }

                                            //careful - we have to make different calls depending on whether we want to override server info.
                                            if (string.IsNullOrEmpty(emailServer))
                                            {
                                                //do not override the server.
                                                packager.SendEmail(SessionCriteria.NewSessions, true, null, fromAddress, destinationAddress, null);
                                            }
                                            else
                                            {
                                                //override the server and all its related options.
                                                packager.SendEmail(SessionCriteria.NewSessions, true, null, fromAddress, destinationAddress, emailServer, port, useSsl, emailUser, emailPassword);
                                            }

                                            break;
                                        case "FILE":
                                            string fullFileNamePath = commandArgs["d"];

                                            if (string.IsNullOrEmpty(fullFileNamePath))
                                            {
                                                returnVal = ExitCodes.MissingFileInfo;
                                                Log.Error("Startup", "Unable to process command line", "There is no file name (-d) specified so the packager can't start.");
                                                Console.WriteLine("There is no file name (-d) specified so the packager can't start.");
                                            }
                                            else
                                            {
                                                packager.SendToFile(SessionCriteria.NewSessions, true, fullFileNamePath);
                                            }
                                            break;
                                        default:
                                            returnVal = ExitCodes.InvalidTransmitMode;
                                            Log.Error("Startup", "Unable to process command line", "Unrecognized transmit mode: {0}.  Try server, email or file", transmitMode);
                                            Console.WriteLine("Unrecognized transmit mode: Try server, email or file");
                                            break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                returnVal = ExitCodes.RuntimeException;
                                Log.RecordException(ex, "Startup", false);
                            }
                        }
                    }
                    else
                    {
                        //we have to use a form to run things, but in our case it's a special form that never shows :)
                        Application.Run(new frmMain(productName, applicationName));
                    }
                }

                return (int)returnVal;
            }
            finally
            {
                Log.EndSession();
            }
        }

        private static void OnLogInitializing(object sender, LogInitializingEventArgs e)
        {
            e.Configuration.Publisher.EnableDebugMode = true;
        }
    }
}