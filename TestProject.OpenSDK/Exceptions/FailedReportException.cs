// <copyright file="FailedReportException.cs" company="TestProject">
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

namespace TestProject.OpenSDK.Exceptions
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Exception object thrown when the SDK is failed to send reports to the agent.
    /// </summary>
    public class FailedReportException : Exception
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="FailedReportException"/> class with the provided message.
        /// </summary>
        /// <param name="message">Exception message to be set.</param>
        public FailedReportException(string message)
            : base(message)
        {
        }
    }
}
