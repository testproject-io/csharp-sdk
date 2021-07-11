// <copyright file="TestReport.cs" company="TestProject">
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
    /// Payload object sent to the Agent when reporting a test.
    /// </summary>
    public class TestReport : Report
    {
        /// <summary>
        /// Define type as Test for batch report support.
        /// </summary>
        [JsonProperty("type")]
        private const ReportItemType Type = ReportItemType.Test;

        /// <summary>
        /// The test name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// True if the test should be marked as passed, false otherwise.
        /// </summary>
        public bool Passed { get; }

        /// <summary>
        /// A message that goes with the test.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestReport"/> class.
        /// </summary>
        /// <param name="name">The test name.</param>
        /// <param name="passed">True if the command was executed successfully, false otherwise.</param>
        /// <param name="message">A message that goes with the test.</param>
        public TestReport(string name, bool passed, string message)
        {
            this.Name = name;
            this.Passed = passed;
            this.Message = message;
        }
    }
}
