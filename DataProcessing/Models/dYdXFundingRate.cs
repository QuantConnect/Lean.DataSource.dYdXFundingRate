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

using Newtonsoft.Json;

namespace QuantConnect.DataProcessing.Models;

/// <summary>
/// Funding rate
/// </summary>
public class dYdXFundingRate
{
    /// <summary>
    /// Ticker symbol
    /// </summary>
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    /// <summary>
    /// Funding rate
    /// </summary>
    [JsonProperty("rate")]
    public decimal Rate { get; set; }

    /// <summary>
    /// Price at the time
    /// </summary>
    [JsonProperty("price")]
    public string Price { get; set; }

    /// <summary>
    /// Effective at height
    /// </summary>
    [JsonProperty("effectiveAtHeight")]
    public string EffectiveAtHeight { get; set; }

    /// <summary>
    /// Effective at timestamp
    /// </summary>
    [JsonProperty("effectiveAt")]
    public string EffectiveAt { get; set; }
}