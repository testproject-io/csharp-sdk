﻿// <copyright file="NUnitAnalyzer.cs" company="TestProject">
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

namespace TestProject.OpenSDK.Internal.CallStackAnalysis
{
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Defines methods that are used to determine whether or not a method belongs to NUnit.
    /// </summary>
    public class NUnitAnalyzer : IMethodAnalyzer
    {
        private const string TestAttribute = "TestAttribute";
        private const string SetUpAttribute = "SetUpAttribute";
        private const string NUnitFrameworkNamespace = "NUnit.Framework";
        private const string TestNameProperty = "Description";

        /// <summary>
        /// Determines whether or not the class containing the method that is run belongs to NUnit.
        /// </summary>
        /// <param name="method">The method to be analyzed.</param>
        /// <returns>True if the class containing the method is an NUnit class, false otherwise.</returns>
        public bool IsTestClass(MethodBase method)
        {
            // NUnit has [Test] attribute on test methods.
            return method.GetCustomAttributes(true)
                .Any(a => a.GetType().Name.Equals(TestAttribute)
                && a.GetType().Namespace.Equals(NUnitFrameworkNamespace));
        }

        /// <summary>
        /// Determines whether or not the given method is run inside an NUnit [SetUp] method.
        /// </summary>
        /// <param name="method">The method to be analyzed.</param>
        /// <returns>True if the method is run inside [SetUp], false otherwise.</returns>
        public bool IsSetupMethod(MethodBase method)
        {
            // NUnit setup methods are identified by [SetUpAttribute] on the method
            return method.GetCustomAttributes(true).Any(a => a.GetType().Name.Contains(SetUpAttribute));
        }

        /// <inheritdoc cref="IMethodAnalyzer"/>
        public string GetTestName(MethodBase method)
        {
            // Attribute has a Description property set this way: [TestMethod(Description = "name")]
            var attribute = method.GetCustomAttributes(true)
                .FirstOrDefault(a => a.GetType().Name.Equals(TestAttribute)
                                     && a.GetType().Namespace.Equals(NUnitFrameworkNamespace));
            return attribute?.GetType().GetProperty(TestNameProperty)?.GetValue(attribute)?.ToString();
        }
    }
}
