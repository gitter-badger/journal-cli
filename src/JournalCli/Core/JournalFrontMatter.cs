﻿using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using JournalCli.Infrastructure;
using NodaTime;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace JournalCli.Core
{
    internal class JournalFrontMatter : IJournalFrontMatter
    {
        public static string BlockIndicator = "---";

        public static JournalFrontMatter FromFilePath(IFileSystem fileSystem, string filePath)
        {
            StringBuilder sb;
            using (var fs = fileSystem.File.OpenText(filePath))
            {
                var firstLine = fs.ReadLine();
                if (firstLine != "---")
                    return new JournalFrontMatter();

                sb = new StringBuilder(firstLine + Environment.NewLine);

                while (!fs.EndOfStream)
                {
                    var next = fs.ReadLine();
                    sb.Append(next + Environment.NewLine);

                    if (next == "---")
                        break;
                }
            }

            var yaml = sb.ToString();
            // TODO: Write test to verify that journal entry files always represent its date
            var journalEntryDate = LocalDate.FromDateTime(DateTime.Parse(fileSystem.Path.GetFileNameWithoutExtension(filePath)));
            return new JournalFrontMatter(yaml, journalEntryDate);
        }

        private JournalFrontMatter() { }

        public JournalFrontMatter(IEnumerable<string> tags, string readme)
        {
            Tags = tags.Distinct().ToList();
            Readme = readme;
        }

        // TODO: Write test to validate that front matter values will never be incorrectly overwritten.
        public JournalFrontMatter(string yamlFrontMatter, LocalDate journalEntryDate)
        {
            yamlFrontMatter = yamlFrontMatter.Trim();
            var yamlLines = yamlFrontMatter.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var originalLineCount = yamlLines.Count;

            if (yamlLines[0] == BlockIndicator)
                yamlLines.RemoveAt(0);

            var lastIndex = yamlLines.Count - 1;
            if (yamlLines[lastIndex] == BlockIndicator)
                yamlLines.RemoveAt(lastIndex);

            if (originalLineCount != yamlLines.Count)
                yamlFrontMatter = string.Join(Environment.NewLine, yamlLines);

            using (var reader = new System.IO.StringReader(yamlFrontMatter))
            {
                var yamlStream = new YamlStream();
                yamlStream.Load(reader);

                var keys = yamlStream.Documents[0].RootNode.AllNodes
                    .Where(x => x.NodeType == YamlNodeType.Mapping)
                    .Cast<YamlMappingNode>()
                    .SelectMany(x => x.Children.Keys)
                    .Cast<YamlScalarNode>()
                    .ToList();

                var tagsKey = keys.FirstOrDefault(k => k.Value.ToLowerInvariant() == "tags");
                var readMeKey = keys.FirstOrDefault(k => k.Value.ToLowerInvariant() == "readme");

                if (tagsKey != null)
                {
                    var tags = (YamlSequenceNode)yamlStream.Documents[0].RootNode[tagsKey];
                    Tags = tags.Select(x => x.ToString()).Distinct().ToList();
                }

                if (readMeKey != null)
                {
                    var readme = (YamlScalarNode)yamlStream.Documents[0].RootNode[readMeKey];
                    var parser = new ReadmeParser(readme.Value, journalEntryDate);
                    Readme = parser.FrontMatterValue;
                    ReadmeDate = parser.ExpirationDate;
                }
            }
        }

#warning TEST: are these attributes working correctly?
        [YamlMember(Alias = "tags")]
        public ICollection<string> Tags { get; }

        [YamlMember(Alias = "readme")]
        public string Readme { get; }

        [YamlIgnore]
        public LocalDate ReadmeDate { get; }

        public string ToString(bool asFrontMatter)
        {
            var serializer = new SerializerBuilder().Build();
            var yaml = serializer.Serialize(this).Replace("- ", "  - ").Trim();
            return asFrontMatter ? $"{BlockIndicator}{Environment.NewLine}{yaml}{Environment.NewLine}{BlockIndicator}" : yaml;
        }

        public override string ToString() => ToString(false);
    }
}