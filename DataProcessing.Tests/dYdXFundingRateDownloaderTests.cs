/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.IO;
using QuantConnect.DataProcessing;

namespace DataProcessing.Tests;

public class Tests
{
    [Test]
    public void ShouldReturnExpectedData()
    {
        // Create temp directory
        var tempFolder = Path.Combine(Path.GetTempPath(), $"dydx_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempFolder);

        try
        {
            // Read expected data from fixtures
            var fixtureFile = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "btcusd.csv");
            var expectedLines = File.ReadAllLines(fixtureFile);

            // Test with three dates from the fixture file: 2026-01-11, 2026-01-12, 2026-01-13
            var deploymentDate = new DateTime(2026, 1, 10);
            using var downloader = new TestdYdXFundingRateDownloader(tempFolder, deploymentDate);
            downloader.Run();

            // Verify output file exists
            var outputFile = Path.Combine(tempFolder, "cryptofuture", "dydx", "margin_interest", "btcusd.csv");
            Assert.That(File.Exists(outputFile), Is.True, "Output file should exist");

            // Read and compare results
            var actualLines = File.ReadAllLines(outputFile);

            // Compare line counts
            Assert.That(actualLines.Length, Is.EqualTo(expectedLines.Length), "Line count should match");

            // Compare content line by line
            for (int i = 0; i < expectedLines.Length; i++)
            {
                Assert.That(actualLines[i], Is.EqualTo(expectedLines[i]), $"Line {i + 1} should match");
            }
        }
        finally
        {
            // Clean up temp folder
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}