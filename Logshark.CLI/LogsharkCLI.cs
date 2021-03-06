﻿using log4net;
using Logshark.Config;
using Logshark.Controller;
using Logshark.Controller.Extraction;
using Logshark.Controller.Metadata.Run;
using Logshark.Exceptions;
using Logshark.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Logshark.CLI
{
    /// <summary>
    /// Command-line wrapper for running Logshark.
    /// </summary>
    public sealed class LogsharkCLI
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly LogsharkConfiguration configuration;
        private readonly LogsharkCommandLineOptions commandLineOptions;
        private readonly string currentWorkingDirectory;

        public LogsharkCLI(LogsharkCommandLineOptions commandLineOptions, string currentWorkingDirectory)
        {
            configuration = LogsharkConfigReader.LoadConfiguration();
            this.commandLineOptions = commandLineOptions;
            this.currentWorkingDirectory = currentWorkingDirectory;
        }

        #region Public Methods

        /// <summary>
        /// Sets up and issues the LogsharkRequest to the LogsharkController.
        /// </summary>
        public void Execute()
        {
            if (commandLineOptions.ListPlugins)
            {
                try
                {
                    LogsharkController.PrintAvailablePlugins();
                    return;
                }
                catch (Exception ex)
                {
                    Log.FatalFormat("Unable to retrieve list of available plugins: {0}", ex.Message);
                    throw;
                }
            }

            try
            {
                LogsharkRequest request = BuildLogsharkRequest(commandLineOptions);
                LogsharkRequestProcessor requestProcessor = InitializeRequestProcessor();
                requestProcessor.ProcessRequest(request);
            }
            catch (Exception ex)
            {
                // Certain known exception types have already had their errors logged out by the core; we want to avoid duplicating error logging on these.
                if (!IsKnownExceptionType(ex))
                {
                    Log.Fatal(ex.GetFlattenedMessage());
                }

                Log.Debug(ex);
                throw;
            }
        }

        #endregion Public Methods

        #region Private Methods

        private LogsharkRequestProcessor InitializeRequestProcessor()
        {
            try
            {
                LogsharkRunMetadataWriter metadataWriter = new LogsharkRunMetadataWriter(configuration.PostgresConnectionInfo);
                return new LogsharkRequestProcessor(metadataWriter);
            }
            catch (Exception ex)
            {
                throw new DatabaseInitializationException("Failed to initialize Logshark request processor!\nPlease check to make sure that the results database was configured correctly.", ex);
            }
        }

        private LogsharkRequest BuildLogsharkRequest(LogsharkCommandLineOptions commandLineArgs)
        {
            string target = commandLineArgs.Target;
            if (String.IsNullOrWhiteSpace(target))
            {
                throw new ArgumentException("No logset target specified! See 'logshark --help' for usage examples.");
            }

            // If the target is a relative path, we first need to convert it to an absolute path.
            if (!LogsetHashUtil.IsValidMD5(target) && !Path.IsPathRooted(target))
            {
                target = Path.Combine(currentWorkingDirectory, target);
            }

            var request = new LogsharkRequestBuilder(target, configuration)
                .WithProjectDescription(commandLineArgs.ProjectDescription)
                .WithProjectName(commandLineArgs.ProjectName)
                .WithSiteName(commandLineArgs.SiteName)
                .WithPostgresDatabaseName(commandLineArgs.DatabaseName)
                .WithForceParse(commandLineArgs.ForceParse)
                .WithProcessDebug(commandLineArgs.ProcessDebug)
                .WithDropParsedLogset(commandLineArgs.DropParsedLogset)
                .WithPublishWorkbooks(commandLineArgs.PublishWorkbooks)
                .WithMetadata(ParseCommandLineArgToDictionary(commandLineArgs.Metadata))
                .WithWorkbookTags(commandLineArgs.WorkbookTags)
                .WithPluginsToExecute(commandLineArgs.Plugins)
                .WithPluginCustomArguments(ParseCommandLineArgToDictionary(commandLineArgs.CustomArgs))
                .WithProcessFullLogset(commandLineArgs.ParseAll)
                .WithStartLocalMongo(commandLineArgs.StartLocalMongo)
                .WithLocalMongoPort(commandLineArgs.LocalMongoPort)
                .WithSource("CLI")
                .GetRequest();

            return request;
        }

        /// <summary>
        /// Parses a collection of command-line arg strings to a dictionary of key/value argument pairs.  String should be in format "argkey1:argval1 argkey2:argval2"
        /// </summary>
        /// <param name="args">A collection of args to parse.</param>
        /// <returns>Dictionary of parsed arguments.</returns>
        private IDictionary<string, object> ParseCommandLineArgToDictionary(IEnumerable<string> args)
        {
            var argCollection = new Dictionary<string, object>();

            foreach (var arg in args)
            {
                string[] keyAndValue = arg.Split(':');
                if (keyAndValue.ToList().Count != 2)
                {
                    throw new ArgumentException("Invalid argument! Custom arguments must be formatted as ArgumentName:ArgumentValue", arg);
                }
                argCollection.Add(keyAndValue[0], keyAndValue[1]);
            }

            return argCollection;
        }

        /// <summary>
        /// Indicates whether an exception is a type known to be thrown by the core LogsharkController.
        /// </summary>
        /// <param name="ex">The exception to check the type of.</param>
        /// <returns>Exception is a known type thrown by the core controller.</returns>
        private static bool IsKnownExceptionType(Exception ex)
        {
            return ex is ExtractionException ||
                   ex is InsufficientDiskSpaceException ||
                   ex is ProcessingException ||
                   ex is InvalidLogsetException ||
                   ex is PublishingException;
        }

        #endregion Private Methods
    }
}