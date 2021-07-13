// <copyright file="StepReport.cs" company="TestProject">
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

namespace TestProject.OpenSDK.Internal.Rest.Messages
{
    using Newtonsoft.Json;

    /// <summary>
    /// Payload object sent to the Agent when reporting a test step.
    /// </summary>
    public class StepReport : Report
    {
        /// <summary>
        /// Define type as Test for batch report support.
        /// </summary>
        [JsonProperty("type")]
        private const ReportItemType Type = ReportItemType.Step;

        /// <summary>
        /// A GUID that uniquely identifies this step.
        /// </summary>
        public string Guid { get; }

        /// <summary>
        /// The step description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// A message that goes with the step.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// True if the step should be marked as passed, false otherwise.
        /// </summary>
        public bool Passed { get; }

        /// <summary>
        /// A base64 encoded screenshot, to be included in the report.
        /// </summary>
        public string Screenshot { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StepReport"/> class.
        /// </summary>
        /// <param name="description">The step description.</param>
        /// <param name="message">A message that goes with the step.</param>
        /// <param name="passed">True if the command was executed successfully, false otherwise.</param>
        /// <param name="screenshot">A base64 encoded screenshot, to be included in the report.</param>
        public StepReport(string description, string message, bool passed, string screenshot)
        {
            this.Guid = System.Guid.NewGuid().ToString();
            this.Description = description;
            this.Message = message;
            this.Passed = passed;
            this.Screenshot = screenshot;
        }
    }
}
