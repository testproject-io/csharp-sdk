// <copyright file="AgentClient.cs" company="TestProject">
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

namespace TestProject.OpenSDK.Internal.Rest
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using NLog;
    using OpenQA.Selenium;
    using OpenQA.Selenium.Appium;
    using OpenQA.Selenium.Chrome;
    using OpenQA.Selenium.Edge;
    using OpenQA.Selenium.Firefox;
    using OpenQA.Selenium.IE;
    using OpenQA.Selenium.Safari;
    using RestSharp;
    using TestProject.OpenSDK.Drivers.Generic;
    using TestProject.OpenSDK.Enums;
    using TestProject.OpenSDK.Exceptions;
    using TestProject.OpenSDK.Internal.Addons;
    using TestProject.OpenSDK.Internal.CallStackAnalysis;
    using TestProject.OpenSDK.Internal.Helpers;
    using TestProject.OpenSDK.Internal.Rest.Messages;
    using TestProject.OpenSDK.Internal.Rest.Messages.SessionResponses;
    using TestProject.OpenSDK.Internal.Tcp;

    /// <summary>
    /// Client used to communicate with the TestProject Agent process.
    /// </summary>
    public class AgentClient
    {
        /// <summary>
        /// Default remote connection timeout.
        /// </summary>
        internal static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Minimum Agent version that support session reuse.
        /// </summary>
        private static readonly Version MinSessionReuseCapableVersion = new Version("0.64.32");

        /// <summary>
        /// The current version of the Agent in use.
        /// </summary>
        private static Version agentVersion;

        /// <summary>
        /// Class member to store Agent session details.
        /// </summary>
        public AgentSession AgentSession { get; private set; }

        /// <summary>
        /// SpecFlow test report.
        /// </summary>
        public TestReport SpecFlowTestReport { get; set; }

        /// <summary>
        /// An instance of the <see cref="AgentClient"/> class.
        /// </summary>
        private static AgentClient instance;

        /// <summary>
        /// Name of the environment variable that stores the development token.
        /// </summary>
        private readonly string tpDevToken = "TP_DEV_TOKEN";

        /// <summary>
        /// Name of the environment variable that stores the Agent address.
        /// </summary>
        private readonly string tpAgentAddress = "TP_AGENT_URL";

        /// <summary>
        /// The default Agent address to be used if no address is specified in the driver constructor or environment variable.
        /// </summary>
        private readonly string agentDefaultAddress = "http://localhost:8585";

        /// <summary>
        /// The default timeout in milliseconds for the <see cref="RestClient"/> class.
        /// </summary>
        private readonly int defaultRestClientTimeoutInMilliseconds = 100 * 1000;

        /// <summary>
        /// Minimum agent version that supports local reports.
        /// </summary>
        private readonly Version minLocalReportSupportedVersion = new Version("2.1.0");

        /// <summary>
        /// The remote address where the Agent is running.
        /// </summary>
        private Uri remoteAddress;

        /// <summary>
        /// The development token used to authenticate with the Agent.
        /// </summary>
        private string token;

        /// <summary>
        /// A REST client instance used to send HTTP requests to the Agent.
        /// </summary>
        private RestClient client;

        /// <summary>
        /// Configuration settings for (de-)serializing objects to / from JSON.
        /// </summary>
        private JsonSerializerSettings serializerSettings;

        /// <summary>
        /// The queue used to submit and report driver command, test and step reports.
        /// </summary>
        private ReportsQueue reportsQueue;

        /// <summary>
        /// Response from the starting of the agent session.
        /// </summary>
        private SessionResponse sessionResponse;

        /// <summary>
        /// Agent Session report settings.
        /// </summary>
        private ReportSettings reportSettings;

        /// <summary>
        /// Logger instance for this class.
        /// </summary>
        private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Returns an instance of the <see cref="AgentClient"/>.
        /// </summary>
        /// <returns>An instance of the <see cref="AgentClient"/>.</returns>
        public static AgentClient GetInstance()
        {
            return instance ?? throw new SdkException("Drivers must be set before getting the AgentClient instance");
        }

        /// <summary>
        /// If necessary, creates and then returns an instance of the <see cref="AgentClient"/>.
        /// </summary>
        /// <param name="remoteAddress">The remote address where the Agent is running.</param>
        /// <param name="token">The development token used to authenticate with the Agent.</param>
        /// <param name="capabilities">Requested driver options for the browser session.</param>
        /// <param name="reportSettings">Contains the project and job name to report to TestProject.</param>
        /// <param name="disableReports">Set to true to disable all reporting to TestProject, false otherwise.</param>
        /// <param name="compatibleVersion">Minimum Agent version that supports the requested feature. Can be used to check Agent compatibility.</param>
        /// <returns>A instance of the <see cref="AgentClient"/>.</returns>
        public static AgentClient GetInstance(Uri remoteAddress, string token, DriverOptions capabilities, ReportSettings reportSettings, bool disableReports, Version compatibleVersion = null)
        {
            lock (typeof(AgentClient))
            {
                if (IsInitialized())
                {
                    string jobName = string.Empty;
                    string projectName = string.Empty;
                    if (reportSettings.JobName == null && reportSettings.ProjectName != null)
                    {
                        jobName = StackTraceHelper.Instance.GetInferredJobName();
                    }
                    else if (reportSettings.ProjectName == null && reportSettings.JobName != null)
                    {
                        projectName = StackTraceHelper.Instance.GetInferredProjectName();
                    }
                    else if (reportSettings.ProjectName == null && reportSettings.JobName == null)
                    {
                        projectName = StackTraceHelper.Instance.GetInferredProjectName();
                        jobName = StackTraceHelper.Instance.GetInferredJobName();
                    }
                    else
                    {
                        jobName = reportSettings.JobName;
                        projectName = reportSettings.ProjectName;
                    }

                    ReportSettings newSettings = new ReportSettings(projectName, jobName);

                    bool sameReportSettings = instance.reportSettings != null && instance.reportSettings.Equals(newSettings);
                    if (!sameReportSettings || !CanReuseSession())
                    {
                        // Close the dev socket as this is not the same session as previous test
                        SocketManager.GetInstance().CloseSocket();
                    }

                    // Stop the current AgentClient instance and submit the test reports.
                    instance.Stop();
                }

                // Create a new AgentClient instance
                instance = new AgentClient(remoteAddress, token, capabilities, reportSettings, disableReports);
            }

            // Return new instance.
            return instance;
        }

        /// <summary>
        /// Helper method to check if an instance has already been created, without actually creating one if it hasn't.
        /// </summary>
        /// <returns>True if the AgentClient has already been initialized, false otherwise.</returns>
        public static bool IsInitialized()
        {
            return instance != null;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentClient"/> class.
        /// </summary>
        /// <param name="remoteAddress">The remote address where the Agent is running.</param>
        /// <param name="token">The development token used to authenticate with the Agent.</param>
        /// <param name="capabilities">Requested driver options for the browser session.</param>
        /// <param name="reportSettings">Contains the project and job name to report to TestProject.</param>
        /// <param name="disableReports">Set to true to disable all reporting to TestProject, false otherwise.</param>
        /// <param name="compatibleVersion">Minimum Agent version that supports the requested feature. Can be used to check Agent compatibility.</param>
        private AgentClient(Uri remoteAddress, string token, DriverOptions capabilities, ReportSettings reportSettings, bool disableReports, Version compatibleVersion = null)
        {
            this.remoteAddress = this.InferRemoteAddress(remoteAddress);

            ReportSettings sessionReportSettings = disableReports ? null : this.InferReportSettings(reportSettings);
            this.reportSettings = sessionReportSettings;

            if (Environment.GetEnvironmentVariable(this.tpDevToken) != null)
            {
                if (token != null)
                {
                    Logger.Info("Found {0} environment variable. Using its value as the development token", this.tpDevToken);
                }

                this.token = Environment.GetEnvironmentVariable(this.tpDevToken);
            }
            else if (token != null)
            {
                this.token = token;
            }
            else
            {
                throw new InvalidTokenException("No token has been provided.");
            }

            this.client = new RestClient(this.remoteAddress);
            this.client.AddDefaultHeader("Authorization", this.token);

            this.serializerSettings = CustomJsonSerializer.Populate(new JsonSerializerSettings());

            // Check that Agent version supports the requested feature.
            if (compatibleVersion != null)
            {
                Logger.Trace($"Checking if the Agent version is {compatibleVersion} at minimum");

                agentVersion = this.GetAgentVersion();

                if (agentVersion.CompareTo(compatibleVersion) < 0)
                {
                    throw new AgentConnectException($"Current Agent version {agentVersion} does not support the requested feature," +
                        $" should be at least {compatibleVersion}");
                }
            }

            this.reportsQueue = new ReportsQueue(this.client);

            this.StartSession(sessionReportSettings, capabilities);

            // Verify the agent version supports local report generation
            this.VerifyIfLocalReportsIsSupported(reportSettings.ReportType);

            // Show local report path only when executing on local agents.
            if (this.client.BaseUrl.IsLocal())
            {
                Logger.Info($"Execution Report: {this.sessionResponse.LocalReport}");
            }

            if (!string.IsNullOrWhiteSpace(this.sessionResponse.LocalReportUrl))
            {
                Logger.Info($"Execution Report Link: {this.sessionResponse.LocalReportUrl}");
            }
        }

        /// <summary>
        /// Reports a WebDriver command to TestProject.
        /// </summary>
        /// <param name="driverCommandReport">Payload object containing command information and execution result.</param>
        public void ReportDriverCommand(DriverCommandReport driverCommandReport)
        {
            RestRequest sendDriverCommandRequest = new RestRequest(Endpoints.REPORT_COMMAND, Method.POST);
            sendDriverCommandRequest.RequestFormat = DataFormat.Json;

            string json = CustomJsonSerializer.ToJson(driverCommandReport, this.serializerSettings);

            sendDriverCommandRequest.AddJsonBody(json);

            this.reportsQueue.Submit(sendDriverCommandRequest, driverCommandReport);
        }

        /// <summary>
        /// Sends a <see cref="TestReport"/> to the Agent.
        /// </summary>
        /// <param name="testReport">The payload object containing the test details to be reported.</param>
        public void ReportTest(TestReport testReport)
        {
            RestRequest sendTestReportRequest = new RestRequest(Endpoints.REPORT_TEST, Method.POST);
            sendTestReportRequest.RequestFormat = DataFormat.Json;

            string json = CustomJsonSerializer.ToJson(testReport, this.serializerSettings);

            sendTestReportRequest.AddJsonBody(json);

            this.reportsQueue.Submit(sendTestReportRequest, testReport);
        }

        /// <summary>
        /// Sends a <see cref="StepReport"/> to the Agent.
        /// </summary>
        /// <param name="stepReport">The payload object containing the step details to be reported.</param>
        public void ReportStep(StepReport stepReport)
        {
            RestRequest sendStepReportRequest = new RestRequest(Endpoints.REPORT_STEP, Method.POST);
            sendStepReportRequest.RequestFormat = DataFormat.Json;

            string json = CustomJsonSerializer.ToJson(stepReport, this.serializerSettings);

            sendStepReportRequest.AddJsonBody(json);

            this.reportsQueue.Submit(sendStepReportRequest, stepReport);
        }

        /// <summary>
        /// Executes a specified action proxy from an addon.
        /// </summary>
        /// <param name="actionProxy">The <see cref="ActionProxy"/> to execute.</param>
        /// <param name="timeout">The action execution timeout (in milliseconds).</param>
        /// <returns>The response from the Agent upon executing the action proxy.</returns>
        public ActionExecutionResponse ExecuteProxy(ActionProxy actionProxy, int timeout)
        {
            RestRequest sendActionProxyRequest = new RestRequest(Endpoints.EXECUTE_ACTION_PROXY, Method.POST);
            sendActionProxyRequest.RequestFormat = DataFormat.Json;

            string json = CustomJsonSerializer.ToJson(actionProxy.ProxyDescriptor, this.serializerSettings);

            sendActionProxyRequest.AddJsonBody(json);

            if (timeout > 0)
            {
                // Since action proxy execution can take a while, you can override the default timeout.
                this.client.Timeout = timeout;
            }

            IRestResponse sendActionProxyResponse = this.client.Execute(sendActionProxyRequest);

            if (sendActionProxyResponse.StatusCode.Equals(HttpStatusCode.NotFound))
            {
                string errorMessage = $"Action {actionProxy.ProxyDescriptor.ClassName} in addon {actionProxy.ProxyDescriptor.Guid} is not installed for your account.";

                Logger.Error(errorMessage);
                throw new AddonNotInstalledException(errorMessage);
            }

            // Reset the client timeout to the RestSharp default.
            this.client.Timeout = this.defaultRestClientTimeoutInMilliseconds;

            return CustomJsonSerializer.FromJson<ActionExecutionResponse>(sendActionProxyResponse.Content, this.serializerSettings);
        }

        /// <summary>
        /// Stops the current development session with the Agent.
        /// </summary>
        public void Stop()
        {
            this.reportsQueue.Stop();
        }

        /// <summary>
        /// Indicates whether the current Agent version supports session reuse.
        /// </summary>
        /// <returns>True if the Agent version supports session reuse, false otherwise.</returns>
        private static bool CanReuseSession()
        {
            if (agentVersion == null)
            {
                return false;
            }

            return agentVersion.CompareTo(MinSessionReuseCapableVersion) >= 0;
        }

        /// <summary>
        /// Starts a new session with the Agent.
        /// </summary>
        /// <param name="reportSettings">Settings (project name, job name) to be included in the report.</param>
        /// <param name="capabilities">Additional options to be applied to the driver instance.</param>
        private void StartSession(ReportSettings reportSettings, DriverOptions capabilities)
        {
            RestRequest startSessionRequest = new RestRequest(Endpoints.DEVELOPMENT_SESSION, Method.POST);
            startSessionRequest.RequestFormat = DataFormat.Json;

            string json = CustomJsonSerializer.ToJson(new SessionRequest(reportSettings, capabilities), this.serializerSettings);

            startSessionRequest.AddJsonBody(json);

            IRestResponse startSessionResponse = this.client.Execute(startSessionRequest);

            if (startSessionResponse.ErrorException != null)
            {
                string errorMessage = $"An error occurred connecting to the Agent. Is your Agent running at {this.remoteAddress}?";

                Logger.Error(errorMessage);
                throw new AgentConnectException(errorMessage);
            }

            if ((int)startSessionResponse.StatusCode >= 400)
            {
                this.HandleSessionStartFailure(startSessionResponse);
                return;
            }

            // Only retrieve the Agent version when it has not yet been set
            if (agentVersion == null)
            {
                agentVersion = this.GetAgentVersion();
            }

            this.StartSdkSession(startSessionResponse, capabilities);
        }

        private void StartSdkSession(IRestResponse startSessionResponse, DriverOptions capabilities)
        {
            if (capabilities is AppiumOptions)
            {
                // Appium capabilities cannot be deserialized directly into an AppiumOptions instance,
                // so we need to do this ourselves
                Logger.Info($"Creating Appium options from capabilities returned by Agent...");

                AppiumSessionResponse sessionResponse = CustomJsonSerializer.FromJson<AppiumSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                AppiumOptions appiumOptions = this.CreateAppiumOptions(sessionResponse.Capabilities);

                this.CreateAgentSession(sessionResponse, appiumOptions);

                this.OpenSocketConnectionUsing(sessionResponse);
            }
            else
            {
                // Capabilities for web browsers and the generic driver can be serialized directly.
                // Since there's a separate Options class for each supported browser, we'll have to
                // differentiate between them as well.
                if (capabilities is ChromeOptions)
                {
                    ChromeSessionResponse sessionResponse = CustomJsonSerializer.FromJson<ChromeSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else if (capabilities is FirefoxOptions)
                {
                    FirefoxSessionResponse sessionResponse = CustomJsonSerializer.FromJson<FirefoxSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else if (capabilities is EdgeOptions)
                {
                    EdgeSessionResponse sessionResponse = CustomJsonSerializer.FromJson<EdgeSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else if (capabilities is SafariOptions)
                {
                    SafariSessionResponse sessionResponse = CustomJsonSerializer.FromJson<SafariSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else if (capabilities is InternetExplorerOptions)
                {
                    IESessionResponse sessionResponse = CustomJsonSerializer.FromJson<IESessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else if (capabilities is GenericOptions)
                {
                    GenericSessionResponse sessionResponse = CustomJsonSerializer.FromJson<GenericSessionResponse>(startSessionResponse.Content, this.serializerSettings);

                    this.CreateAgentSession(sessionResponse, sessionResponse.Capabilities);

                    this.OpenSocketConnectionUsing(sessionResponse);
                }
                else
                {
                    throw new SdkException($"The options type '{capabilities.GetType().Name}' is not supported by the OpenSDK.");
                }
            }
        }

        private void CreateAgentSession(SessionResponse sessionResponse, DriverOptions options)
        {
            // A generic driver session request returns a partial response, so we'll need to fill in the server address and session ID ourselves.
            Uri serverAddress = sessionResponse.ServerAddress.Equals(string.Empty) ? null : new Uri(sessionResponse.ServerAddress);

            if (sessionResponse.SessionId == null)
            {
                sessionResponse.SessionId = Guid.NewGuid().ToString();
            }

            this.AgentSession = new AgentSession(serverAddress, sessionResponse.SessionId, sessionResponse.Dialect, options);

            Logger.Info($"Session [{sessionResponse.SessionId}] initialized");
            this.sessionResponse = sessionResponse;
            if (this.sessionResponse.Warnings != null)
            {
               foreach (string warning in this.sessionResponse.Warnings)
                {
                    Logger.Warn(warning);
                }
            }
        }

        private void OpenSocketConnectionUsing(SessionResponse sessionResponse)
        {
            SocketManager.GetInstance().OpenSocket(this.remoteAddress.Host, sessionResponse.DevSocketPort, this.sessionResponse.Uuid);
        }

        private AppiumOptions CreateAppiumOptions(Dictionary<string, object> caps)
        {
            if (caps.Count == 0)
            {
                return new AppiumOptions();
            }

            AppiumOptions options = new AppiumOptions();

            foreach (KeyValuePair<string, object> entry in caps)
            {
                options.AddAdditionalCapability(entry.Key, entry.Value);
            }

            return options;
        }

        /// <summary>
        /// Handle any failures that might occur whenever the session request results in an error returned by the Agent.
        /// </summary>
        /// <param name="startSessionResponse">The session response object returned by the Agent.</param>
        private void HandleSessionStartFailure(IRestResponse startSessionResponse)
        {
            JObject responseBody = JObject.Parse(startSessionResponse.Content);
            string responseMessage = string.Empty;

            if (responseBody.ContainsKey("message"))
            {
                responseMessage = (string)responseBody.GetValue("message");
            }
            else
            {
                Logger.Error("Failed to read message from Agent response");
            }

            switch ((int)startSessionResponse.StatusCode)
            {
                case 401:
                    Logger.Error("Failed to initialize a session with the Agent - invalid developer token supplied");
                    Logger.Error("Get your developer token from https://app.testproject.io/#/integrations/sdk?lang=CSharp" +
                        " and set it in the TP_DEV_TOKEN environment variable");
                    throw new InvalidTokenException(responseMessage);
                case 404:
                    Logger.Error("Failed to initialize a session with the Agent - requested browser or mobile device not found on this system");
                    throw new MissingBrowserOrDeviceException(responseMessage);
                case 406:
                    Logger.Error("Failed to initialize a session with the Agent - obsolete SDK version");
                    throw new ObsoleteVersionException(responseMessage);
                default:
                    Logger.Error("Failed to initialize a session with the Agent");
                    throw new AgentConnectException($"Agent responsed with status code {(int)startSessionResponse.StatusCode}: [{responseMessage}]");
            }
        }

        /// <summary>
        /// Retrieves the version of the Agent currently in use.
        /// </summary>
        /// <returns>An instance of the <see cref="Version"/> class containing the Agent version.</returns>
        private Version GetAgentVersion()
        {
            RestRequest getAgentStatusRequest = new RestRequest(Endpoints.STATUS, Method.GET);

            IRestResponse getAgentStatusResponse = this.client.Execute(getAgentStatusRequest);

            if (getAgentStatusResponse.ErrorException != null)
            {
                string errorMessage = $"An error occurred connecting to the Agent. Is your Agent running at {this.remoteAddress}?";

                Logger.Error(errorMessage);
                throw new AgentConnectException(errorMessage);
            }

            if ((int)getAgentStatusResponse.StatusCode >= 400)
            {
                throw new AgentConnectException($"Failed to get Agent status: {getAgentStatusResponse.ErrorMessage}");
            }

            AgentStatusResponse agentStatusResponse = CustomJsonSerializer.FromJson<AgentStatusResponse>(getAgentStatusResponse.Content, this.serializerSettings);

            Logger.Info($"Current Agent version is {agentStatusResponse.Tag}");

            return new Version(agentStatusResponse.Tag);
        }

        private Uri InferRemoteAddress(Uri originalUri)
        {
            if (originalUri != null)
            {
                return originalUri.LocalhostTo127001();
            }

            if (Environment.GetEnvironmentVariable(this.tpAgentAddress) != null)
            {
                return new Uri(Environment.GetEnvironmentVariable(this.tpAgentAddress)).LocalhostTo127001();
            }
            else
            {
                return new Uri(this.agentDefaultAddress).LocalhostTo127001();
            }
        }

        /// <summary>
        /// Tries to infer report settings using the <see cref="StackTraceHelper"/> and returns resulting reporting settings.
        /// </summary>
        /// <param name="originalSettings">The explicitly specified project and job names (may be null or empty) and the report type if set.</param>
        /// <returns><see cref="ReportSettings"/> instance with inferred project and job names (where applicable).</returns>
        private ReportSettings InferReportSettings(ReportSettings originalSettings)
        {
            if (originalSettings != null &&
                !string.IsNullOrEmpty(originalSettings.JobName) &&
                !string.IsNullOrEmpty(originalSettings.ProjectName))
            {
                Logger.Trace("Project and job names were explicitly specified, skipping inferring.");
                Logger.Trace($"Report Type was set to {originalSettings.ReportType}");
                return originalSettings;
            }

            Logger.Trace("Report settings were not provided or incomplete, trying to infer them...");

            string projectName = StackTraceHelper.Instance.GetInferredProjectName();
            string jobName = StackTraceHelper.Instance.GetInferredJobName();

            ReportSettings inferredReportSettings;

            if (originalSettings == null)
            {
                // Create ReportSettings
                inferredReportSettings = new ReportSettings(projectName, jobName, ReportType.CLOUD_AND_LOCAL);
            }
            else
            {
                // Overwrite empty values with inferred ones, Report type is either the default value or supplied one
                inferredReportSettings = new ReportSettings(
                    string.IsNullOrEmpty(originalSettings.ProjectName) ? projectName : originalSettings.ProjectName,
                    string.IsNullOrEmpty(originalSettings.JobName) ? jobName : originalSettings.JobName,
                    originalSettings.ReportType,
                    originalSettings.ReportName,
                    originalSettings.ReportPath);
            }

            Logger.Trace($"Using inferred values '{inferredReportSettings.ProjectName}' and '{inferredReportSettings.JobName}' as project and job name, respectively.");
            Logger.Trace($"Report Type was set to {inferredReportSettings.ReportType}.");

            return inferredReportSettings;
        }

        /// <summary>
        /// Verify whether the current Agent version supports local report generation.
        /// </summary>
        /// <param name="reportType">The request Report Type.</param>
        /// <throws>AgentConnectionException if the current agent version older than <see cref="minLocalReportSupportedVersion"/>.</returns>
        private void VerifyIfLocalReportsIsSupported(ReportType reportType)
        {
            if (reportType == ReportType.LOCAL && agentVersion.CompareTo(this.minLocalReportSupportedVersion) < 0)
            {
                StringBuilder message = new StringBuilder()
                    .Append("Target Agent version")
                    .Append(" [").Append(agentVersion)
                    .Append("] ")
                    .Append("doesn't support local reports. ")
                    .Append("Upgrade the Agent to the latest version and try again.");
                throw new AgentConnectException(message.ToString());
            }
        }

        /// <summary>
        /// Internal class used to store Agent API endpoints.
        /// </summary>
        internal static class Endpoints
        {
            /// <summary>
            /// Endpoint for retrieving Agent status info.
            /// </summary>
            public const string STATUS = "/api/status";

            /// <summary>
            /// Endpoint for starting a new development session.
            /// </summary>
            public const string DEVELOPMENT_SESSION = "/api/development/session";

            /// <summary>
            /// Endpoint for reporting a driver command.
            /// </summary>
            public const string REPORT_COMMAND = "/api/development/report/command";

            /// <summary>
            /// Endpoint for reporting a test.
            /// </summary>
            public const string REPORT_TEST = "/api/development/report/test";

            /// <summary>
            /// Endpoint for reporting a step.
            /// </summary>
            public const string REPORT_STEP = "/api/development/report/step";

            /// <summary>
            /// Endpoint for reporting a batch.
            /// </summary>
            public const string REPORT_BATCH = "/api/development/report/batch";

            /// <summary>
            /// Endpoint for execution action proxies.
            /// </summary>
            public const string EXECUTE_ACTION_PROXY = "/api/addons/executions";
        }
    }
}
