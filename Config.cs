using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace iText5PDFBuilder
{
    [JsonObject]
    public class Config
    {
        [JsonProperty]
        public string title;

        [JsonProperty]
        public string website;

        [JsonProperty]
        public string keywords;

        [JsonProperty]
        public string creator;

        [JsonProperty]
        public string imageUrl;

        [JsonProperty]
        public string overlayUrl;

        [JsonProperty]
        public string subHeader1;

        [JsonProperty]
        public string subHeader2;

        [JsonProperty]
        public string body;

        [JsonProperty]
        public string footer;

        [JsonProperty]
        public string outputFile;
    }
}
