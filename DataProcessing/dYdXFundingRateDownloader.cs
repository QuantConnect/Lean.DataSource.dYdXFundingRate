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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.DataProcessing.Models;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.DataProcessing;

/// <summary>
/// dYdXFundingRateDownloader implementation
/// </summary>
public class dYdXFundingRateDownloader : IDisposable
{
    private readonly DateTime? _deploymentDate;
    private readonly string _destinationFolder;
    private readonly string _existingInDataFolder;
    private readonly HttpClient _client;

    /// <summary>
    /// Control the rate of download per unit of time.
    /// </summary>
    private readonly RateGate _indexGate = new(25, TimeSpan.FromSeconds(10));

    private readonly string[] _perpetualMarkets;

    /// <summary>
    /// Creates a new instance of <see cref="DYdXFundingRateDownloader"/>
    /// </summary>
    /// <param name="destinationFolder">The folder where the data will be saved</param>
    /// <param name="deploymentDate"></param>
    public dYdXFundingRateDownloader(string destinationFolder, DateTime? deploymentDate)
    {
        _deploymentDate = deploymentDate;
        _destinationFolder = Path.Combine(destinationFolder, "cryptofuture", "dydx", "margin_interest");
        _existingInDataFolder = Path.Combine(Globals.DataFolder, "cryptofuture", "dydx", "margin_interest");
        var baseUrl = Config.Get("indexer-rest-api-base-url", "https://indexer.dydx.trade/v4");
        _client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };

        Directory.CreateDirectory(_destinationFolder);

        _perpetualMarkets = GetPerpetualMarkets();
    }

    /// <summary>
    /// Runs the instance of the object.
    /// </summary>
    /// <returns>True if process all downloads successfully</returns>
    public bool Run()
    {
        var ratePerSymbol = new Dictionary<string, Dictionary<DateTime, decimal>>();
        using HttpClient client = new();
        foreach (var date in GetProcessingDates())
        {
            foreach (var marketFundingRate in GetData(date))
            {
                if (!ratePerSymbol.TryGetValue(marketFundingRate.Key, out var dictionary))
                {
                    ratePerSymbol[marketFundingRate.Key] = dictionary = new();
                }

                var count = 0;
                foreach (var fundingRate in marketFundingRate.Value)
                {
                    var fundingTime = fundingRate.EffectiveAt;

                    // Filter by deployment date if specified
                    if (_deploymentDate.HasValue && fundingTime.Date != _deploymentDate.Value.Date)
                    {
                        continue;
                    }

                    var key = new DateTime(fundingTime.Year, fundingTime.Month, fundingTime.Day, fundingTime.Hour,
                        fundingTime.Minute, fundingTime.Second);
                    dictionary[key] = fundingRate.Rate;
                    count++;
                }

                Log.Trace(
                    $"{nameof(dYdXFundingRateDownloader)}.{nameof(Run)}(): Processed {count} funding rates for {marketFundingRate.Key}");
            }
        }

        foreach (var kvp in ratePerSymbol)
        {
            if (kvp.Value.Count > 0)
            {
                SaveContentToFile(_destinationFolder, kvp.Key.Replace("-", ""), kvp.Value);
                Log.Trace(
                    $"{nameof(dYdXFundingRateDownloader)}.{nameof(Run)}(): Saved {kvp.Value.Count} rates for {kvp.Key}");
            }
        }

        return true;
    }

    private IReadOnlyDictionary<string, dYdXFundingRate[]> GetData(DateTime date)
    {
        var start = date.Date;
        var end = date.AddDays(1).Date;

        var result = new ConcurrentDictionary<string, dYdXFundingRate[]>();

        Parallel.ForEach(_perpetualMarkets, ticker =>
        {
            _indexGate.WaitToProceed();
            var url = $"historicalFunding/{ticker}?limit=24&effectiveBeforeOrAt={end:yyyy-MM-ddTHH:mm:ssZ}";

            try
            {
                var data = _client.DownloadData(url);
                var response = data.DeserializeJson<dYdXFundingResponse>();

                if (response?.HistoricalFunding != null)
                {
                    result[ticker] = response.HistoricalFunding;
                }

                Log.Trace(
                    $"{nameof(GetData)}(): Downloaded {response?.HistoricalFunding?.Length ?? 0} rates for {ticker}");
            }
            catch (Exception e)
            {
                Log.Error($"{nameof(GetData)}(): Failed to get data for {ticker}: {e.Message}");
            }
        });
        return result;
    }

    protected virtual string[] GetPerpetualMarkets()
    {
        try
        {
            _indexGate.WaitToProceed();
            var data = _client.DownloadData("perpetualMarkets");
            var response = data.DeserializeJson<dYdXMarketsResponse>();


            return response.Markets
                .Where(m => m.Value.Status == "ACTIVE")
                .Select(m => m.Key)
                .Where(IsTickerValid)
                .ToArray();
        }
        catch (Exception e)
        {
            Log.Error($"GetPerpetualMarkets(): Failed to get markets: {e.Message}");
            return Array.Empty<string>();
        }
    }

    private IEnumerable<DateTime> GetProcessingDates()
    {
        if (_deploymentDate.HasValue)
        {
            return [_deploymentDate.Value];
        }

        // everything
        // return Time.EachDay(new DateTime(2023, 10, 18), DateTime.UtcNow.Date);
        return Time.EachDay(new DateTime(2026, 1, 11), DateTime.UtcNow.Date);
    }

    /// <summary>
    /// Instruments can be with commas in ticker symbols,
    /// i.e. https://dydx.trade/trade/FARTCOIN,RAYDIUM,9BB6NFECJBCTNNLFKO2FQVQBQ8HHM13KCYYCDQBGPUMP-USD
    /// </summary>
    /// <param name="ticker"></param>
    /// <returns></returns>
    private static bool IsTickerValid(string ticker) => !ticker.Contains(",");

    /// <summary>
    /// Saves contents to disk, deleting existing zip files
    /// </summary>
    /// <param name="destinationFolder">Final destination of the data</param>
    /// <param name="name">file name</param>
    /// <param name="contents">Contents to write</param>
    private void SaveContentToFile(string destinationFolder, string name, Dictionary<DateTime, decimal> contents)
    {
        name = name.ToLowerInvariant();
        var finalPath = Path.Combine(destinationFolder, $"{name}.csv");
        var existingPath = Path.Combine(_existingInDataFolder, $"{name}.csv");

        if (File.Exists(existingPath))
        {
            foreach (var line in File.ReadAllLines(existingPath))
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length == 1)
                {
                    continue;
                }

                var time = DateTime.ParseExact(parts[0], "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None);
                var rate = decimal.Parse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture);
                if (!contents.ContainsKey(time))
                {
                    // use existing unless we have a new value
                    contents[time] = rate;
                }
            }
        }

        var finalLines = contents.OrderBy(x => x.Key)
            .Select(x => $"{x.Key:yyyyMMdd HH:mm:ss},{x.Value.ToStringInvariant()}").ToList();

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tmp");
        File.WriteAllLines(tempPath, finalLines);
        var tempFilePath = new FileInfo(tempPath);
        tempFilePath.MoveTo(finalPath, true);
    }

    /// <summary>
    /// Disposes of unmanaged resources
    /// </summary>
    public void Dispose()
    {
        _indexGate.DisposeSafely();
        _client.DisposeSafely();
    }
}