using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace WeaponBuildMaster
{
    public class LocaleData
    {
        [JsonProperty("Language")]
        public string Language { get; set; }

        [JsonProperty("Translate")]
        public Dictionary<string, string> Translate { get; set; }
    }
}
