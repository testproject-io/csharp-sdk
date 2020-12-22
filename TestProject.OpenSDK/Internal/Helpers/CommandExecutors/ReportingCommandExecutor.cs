﻿// <copyright file="ReportingCommandExecutor.cs" company="TestProject">
// Copyright 2020 TestProject (https://testproject.io)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

using System;
using System.Collections.Generic;
using NLog;
using OpenQA.Selenium.Remote;
using TestProject.OpenSDK.Internal.Rest;
using TestProject.OpenSDK.Internal.Rest.Messages;

namespace TestProject.OpenSDK.Internal.Helpers.CommandExecutors
{
    /// <summary>
    /// Reports commands executed to Agent.
    /// </summary>
    public class ReportingCommandExecutor
    {
        /// <summary>
        /// Flag to enable / disable all reporting.
        /// </summary>
        public bool ReportsDisabled { get; set; }

        /// <summary>
        /// Flag to enable / disable automatic driver command reporting.
        /// </summary>
        public bool CommandReportsDisabled { get; set; }

        /// <summary>
        /// Flag to enable / disable automatic test reporting.
        /// </summary>
        public bool AutoTestReportsDisabled { get; set; }

        /// <summary>
        /// Flag to enable / disable command reporting.
        /// </summary>
        public bool RedactionDisabled { get; set; }

        private CustomHttpCommandExecutor commandExecutor;

        /// <summary>
        /// Logger instance for this class.
        /// </summary>
        private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes a new instance of the <see cref="ReportingCommandExecutor"/> class.
        /// </summary>
        /// <param name="commandExecutor">The HTTP command executor used to send WebDriver commands.</param>
        /// <param name="disableReports">True if all reporting should be disabled, false otherwise.</param>
        public ReportingCommandExecutor(CustomHttpCommandExecutor commandExecutor, bool disableReports)
        {
            this.commandExecutor = commandExecutor;
            this.ReportsDisabled = disableReports;
        }

        /// <summary>
        /// Report an executed WebDriver command and its result to TestProject.
        /// </summary>
        /// <param name="command">The WebDriver <see cref="Command"/> to report.</param>
        /// <param name="response">The <see cref="Response"/> from the WebDriver server upon sending the command.</param>
        public void ReportCommand(Command command, Response response)
        {
            bool isQuitCommand = command.Name.Equals(DriverCommand.Quit);

            Dictionary<string, object> result;

            try
            {
                result = (Dictionary<string, object>)response.Value;
            }
            catch (InvalidCastException)
            {
                result = new Dictionary<string, object>();
            }

            if (!isQuitCommand)
            {
                if (!this.RedactionDisabled)
                {
                    command = RedactHelper.RedactCommand(this.commandExecutor, command);
                }

                if (!this.CommandReportsDisabled)
                {
                    this.SendCommandToAgent(command.Name, command.Parameters, result, response.IsPassed());
                }
                else
                {
                    Logger.Trace($"Command '{command.Name}' {(response.IsPassed() ? "passed" : "failed")}");
                }
            }
        }

        /// <summary>
        /// Creates a screenshot (PNG) and returns it as a base64 encoded string.
        /// </summary>
        /// <returns>The base64 encoded screenshot in PNG format.</returns>
        public string GetScreenshot()
        {
            string sessionId = AgentClient.GetInstance().AgentSession.SessionId;

            Dictionary<string, object> screenshotCommandParameters = new Dictionary<string, object>();
            screenshotCommandParameters.Add("sessionId", sessionId);

            Command screenshotCommand = new Command(new SessionId(sessionId), DriverCommand.Screenshot, screenshotCommandParameters);

            Response response = this.commandExecutor.Execute(screenshotCommand, true);

            return response.Value.ToString();
        }

        /// <summary>
        /// Send an executed WebDriver command to the Agent.
        /// </summary>
        /// <param name="commandName">The name of the WebDriver command that was executed.</param>
        /// <param name="commandParams">The corresponding command parameters.</param>
        /// <param name="result">The result of the command execution.</param>
        /// <param name="passed">True if command execution was successful, false otherwise.</param>
        private void SendCommandToAgent(string commandName, Dictionary<string, object> commandParams, Dictionary<string, object> result, bool passed)
        {
            if (commandName.Equals(DriverCommand.Quit))
            {
                // TODO: auto report a test if automatic test reporting is not disabled
                return;
            }

            // TODO: add command redaction
            // TODO: add logic to detect if we're inside a WebDriverWait
            DriverCommandReport driverCommandReport = new DriverCommandReport(commandName, commandParams, result, passed);

            // TODO: add screenshot if command was failed
            // TODO: add command stashing logic
            AgentClient.GetInstance().ReportDriverCommand(driverCommandReport);
        }
    }
}
