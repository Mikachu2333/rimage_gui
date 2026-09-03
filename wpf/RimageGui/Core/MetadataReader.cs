using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace RimageGui.Core
{
    /// <summary>
    /// Reads the JSON file rimage writes for <c>--metadata</c>. This is the
    /// authoritative record of which output each input produced; the GUI never
    /// treats its own predicted path as truth when metadata is available.
    /// </summary>
    public static class MetadataReader
    {
        [DataContract]
        private sealed class MetadataDto
        {
            [DataMember(Name = "images")]
            public ImageDto[] Images { get; set; }
        }

        [DataContract]
        private sealed class ImageDto
        {
            [DataMember(Name = "input")]
            public string Input { get; set; }

            [DataMember(Name = "output")]
            public string Output { get; set; }
        }

        /// <summary>
        /// Maps normalised input key to the output path rimage reported.
        /// Returns null when the file is missing or unparsable, which the caller
        /// treats as "no metadata" rather than "no outputs".
        /// </summary>
        public static Dictionary<string, string> LoadOutputMap(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(MetadataDto));
                    var dto = (MetadataDto)serializer.ReadObject(stream);
                    if (dto?.Images == null)
                    {
                        return null;
                    }

                    var map = new Dictionary<string, string>(dto.Images.Length, StringComparer.Ordinal);
                    foreach (var image in dto.Images)
                    {
                        if (string.IsNullOrEmpty(image?.Input) || string.IsNullOrEmpty(image.Output))
                        {
                            continue;
                        }

                        // Last write wins; rimage emits one entry per input.
                        map[PathUtil.Key(image.Input)] = image.Output;
                    }

                    return map;
                }
            }
            catch (Exception)
            {
                // Corrupt or partially written metadata is a normal outcome when
                // rimage was killed mid-run, so it is not an error path.
                return null;
            }
        }
    }
}
