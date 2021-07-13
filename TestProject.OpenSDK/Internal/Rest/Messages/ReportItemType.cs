// <copyright file="SessionRequest.cs" company="TestProject">
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
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Those are the reports types which are supported to be sent at batches.
    /// </summary>
    public enum ReportItemType
    {
        /// <summary>
        /// Represent command report.
        /// </summary>
        Command,

        /// <summary>
        /// Represent test report.
        /// </summary>
        Test,

        /// <summary>
        /// Represent step report.
        /// </summary>
        Step,
    }
}
