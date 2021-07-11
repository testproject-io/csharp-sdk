// <copyright file="ReportsQueue.cs" company="TestProject">
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

namespace TestProject.OpenSDK.Internal
{
    using System.Collections.Concurrent;
    using System.Threading;
    using NLog;
    using RestSharp;
    using TestProject.OpenSDK.Exceptions;
    using TestProject.OpenSDK.Internal.Rest.Messages;

    /// <summary>
    /// A queue class managing the sending of report items to the Agent in a separate thread.
    /// </summary>
    public class ReportsQueue
    {
        /// <summary>
        /// In case of failure during report - attempt maximum 4 times.
        /// </summary>
        protected const int MaxReportFailureAttempts = 4;

        /// <summary>
        /// Queue that will hold the items to be reported.
        /// </summary>
        protected BlockingCollection<QueueItem> ReportItems { get; } = new BlockingCollection<QueueItem>();

        /// <summary>
        /// HTTP client used to send reports to the Agent.
        /// </summary>
        protected RestClient Client { get; set; }

        /// <summary>
        /// In case of failure.
        /// </summary>
        protected bool BrokenReports { get; set; } = false;

        /// <summary>
        /// Maximum amount of time to wait in milliseconds before forcibly terminating the queue.
        /// </summary>
        private const int REPORTS_QUEUE_TIMEOUT = 10000;

        /// <summary>
        /// Thread that will take care of reporting while test execution continues.
        /// </summary>
        private Thread reporterThread;

        /// <summary>
        /// True if the reporting queue is active, false otherwise.
        /// </summary>
        private bool running;

        /// <summary>
        /// Logger instance for this class.
        /// </summary>
        private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Initializes a new instance of the <see cref="ReportsQueue"/> class.
        /// </summary>
        /// <param name="client">The <see cref="RestClient"/> HTTP client to send reports to the Agent.</param>
        public ReportsQueue(RestClient client)
        {
            this.Client = client;
            this.reporterThread = new Thread(new ThreadStart(this.Worker));
            this.reporterThread.IsBackground = true;
            this.reporterThread.Start();
        }

        /// <summary>
        /// Adds a report to the queue.
        /// </summary>
        /// <param name="request">The <see cref="RestRequest"/> HTTP request to send to the Agent.</param>
        /// <param name="report">The report that this HTTP request contains.</param>
        public void Submit(RestRequest request, Report report)
        {
            if (!this.BrokenReports)
            {
                this.ReportItems.Add(new QueueItem(request, report));
            }
        }

        /// <summary>
        /// Sends all remaining items to the Agent and stops the thread.
        /// </summary>
        public void Stop()
        {
            this.running = false;

            this.ReportItems.Add(new QueueItem(null, null));

            this.ReportItems.CompleteAdding();

            // Wait until all pending items are reported or the timeout has been reached.
            bool reportingCompleted = SpinWait.SpinUntil(() => this.ReportItems.Count == 0, REPORTS_QUEUE_TIMEOUT);

            if (!reportingCompleted)
            {
                Logger.Warn($"There are {this.ReportItems.Count} unreported items left in the queue.");
            }
        }

        /// <summary>
        ///  Handle the report.
        ///  From version 3.1.0 -> send reports in batches.
        ///  For lower versions -> Send standalone report.
        /// </summary>
        protected virtual void HandleReport()
        {
            foreach (QueueItem itemToReport in this.ReportItems.GetConsumingEnumerable(CancellationToken.None))
            {
                this.SendReport(itemToReport);
            }
        }

        /// <summary>
        /// Worker method that polls the queue and waits for and sends new items to report.
        /// </summary>
        private void Worker()
        {
            this.running = true;

            while (this.running || this.ReportItems.Count > 0)
            {
                try
                {
                    this.HandleReport();
                } catch (AgentConnectException)
                {
                    this.BrokenReports = true;
                }
            }
        }

        /// <summary>
        /// Submits a report to the Agent.
        /// </summary>
        /// <param name="itemToReport">The <see cref="QueueItem"/> to report to the Agent.</param>
        /// <exception>AgentConnectionException if cannot send report to the agent more than <see cref="MaxReportFailureAttempts"/> attempts.</exception>
        private void SendReport(QueueItem itemToReport)
        {
            if (itemToReport.Request == null && itemToReport.Report == null)
            {
                if (this.running)
                {
                    // These nulls are not OK, something went wrong preparing the report/request.
                    Logger.Error("An empty request and report were submitted to the queue");
                }

                // These nulls are OK, they were added by stop() method on purpose.
                return;
            }

            int reportAttemtsCount = MaxReportFailureAttempts;
            IRestResponse response;
            do
            {
                response = this.Client.Execute(itemToReport.Request);

                if ((int)response.StatusCode >= 400)
                {
                    Logger.Error($"Agent returned HTTP {(int)response.StatusCode} with message: {response.ErrorMessage}");
                    reportAttemtsCount--;
                    if (reportAttemtsCount == 0)
                    {
                        Logger.Error($"Failed to send reports to the agent.");
                        throw new AgentConnectException($"Failed to send reports to the agent.");
                    }

                    Logger.Error($"Attempt to send report again to the Agent. {reportAttemtsCount} more attempts are left.");
                }
            }
            while (response != null && (int)response.StatusCode >= 400);
        }

        /// <summary>
        /// Internal class to keep the HTTP request and contained report together in the queue.
        /// </summary>
        public class QueueItem
        {
            /// <summary>
            /// The HTTP request containing the report.
            /// </summary>
            public RestRequest Request { get; }

            /// <summary>
            /// The report that will be sent to the Agent.
            /// </summary>
            public Report Report { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="QueueItem"/> class.
            /// </summary>
            /// <param name="request">The <see cref="RestRequest"/> HTTP request to send to the Agent.</param>
            /// <param name="report">The report that this HTTP request contains.</param>
            public QueueItem(RestRequest request, Report report)
            {
                this.Request = request;
                this.Report = report;
            }
        }
    }
}
