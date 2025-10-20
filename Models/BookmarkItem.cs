using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Google_Bookmarks_Manager_for_GPOs.Models
{
    public class BookmarkItem
    {
        [JsonPropertyName("toplevel_name")]
        [XmlElement("toplevel_name")]
        public string? TopLevelName { get; set; }

        [JsonPropertyName("name")]
        [XmlElement("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        [XmlElement("url")]
        public string? Url { get; set; }

        [JsonPropertyName("toplevel")]
        [XmlIgnore]
        public bool? TopLevel { get; set; }

        [JsonPropertyName("children")]
        [XmlArray("children")]
        [XmlArrayItem("dict")]
        public List<BookmarkItem>? Children { get; set; }
    }
}
