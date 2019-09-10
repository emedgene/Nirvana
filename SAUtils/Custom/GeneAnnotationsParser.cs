﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ErrorHandling.Exceptions;
using OptimizedCore;
using SAUtils.GeneIdentifiers;
using SAUtils.Schema;
using VariantAnnotation.Interface.SA;
using VariantAnnotation.SA;

namespace SAUtils.Custom
{
    public sealed class GeneAnnotationsParser : IDisposable
    {
        private readonly StreamReader _reader;
        private readonly Dictionary<string, string> _entrezGeneIdToSymbol;
        private readonly Dictionary<string, string> _ensemblIdToSymbol;

        public string JsonTag;
        private string[] _tags;
        internal CustomAnnotationCategories[] Categories;
        internal string[] Descriptions;
        internal SaJsonValueType[] ValueTypes;
        internal readonly List<string> JsonKeys = new List<string>();
        public SaJsonSchema JsonSchema;

        private const int NumRequiredColumns = 2;
        private int _numAnnotationColumns;
        private Action<string, string>[] _annotationValidators;

        internal GeneAnnotationsParser(StreamReader reader, Dictionary<string, string> entrezGeneIdToSymbol, Dictionary<string, string> ensemblIdToSymbol)
        {
            _reader = reader;
            _entrezGeneIdToSymbol = entrezGeneIdToSymbol;
            _ensemblIdToSymbol = ensemblIdToSymbol;
        }

        public static GeneAnnotationsParser Create(StreamReader reader, Dictionary<string, string> entrezGeneIdToSymbol, Dictionary<string, string> ensemblIdToSymbol)
        {
            var parser = new GeneAnnotationsParser(reader, entrezGeneIdToSymbol, ensemblIdToSymbol);

            parser.ParseHeaderLines();
            parser.InitiateSchema();
            parser.AddHeaderAnnotation();

            return parser;
        }

        internal void ParseHeaderLines()
        {
            JsonTag = ParserUtilities.ParseTitle(_reader.ReadLine());
            _tags = ParserUtilities.ParseTags(_reader.ReadLine(), "#geneSymbol", NumRequiredColumns, "second");
            CheckTagsAndSetJsonKeys();
            Categories = ParserUtilities.ParseCategories(_reader.ReadLine(), NumRequiredColumns, _numAnnotationColumns, _annotationValidators, "third");
            Descriptions = ParserUtilities.ParseDescriptions(_reader.ReadLine(), NumRequiredColumns, _numAnnotationColumns, "forth");
            ValueTypes = ParserUtilities.ParseTypes(_reader.ReadLine(), NumRequiredColumns, _numAnnotationColumns, "fifth");
        }

        private void InitiateSchema()
        {
            JsonSchema = SaJsonSchema.Create(new StringBuilder(), JsonTag, SaJsonValueType.Object, JsonKeys);
        }

        private void CheckTagsAndSetJsonKeys()
        {

            for (int i = NumRequiredColumns; i < _tags.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_tags[i]))
                    throw new UserErrorException($"Please provide a name for column {i + 1} at the second row.");

                JsonKeys.Add(_tags[i]);
            }

            _numAnnotationColumns = _tags.Length - NumRequiredColumns;
            _annotationValidators = Enumerable.Repeat<Action<string, string>>((a, b) => { }, _numAnnotationColumns).ToArray();
        }

        private void AddHeaderAnnotation()
        {
            for (var i = 0; i < _numAnnotationColumns; i++)
            {
                var annotation = SaJsonKeyAnnotation.CreateFromProperties(ValueTypes[i], Categories[i], Descriptions[i]);

                JsonSchema?.AddAnnotation(_tags[i + NumRequiredColumns], annotation);
            }
        }

        public Dictionary<string, List<ISuppGeneItem>> GetItems()
        {
            var geneAnnotations = new Dictionary<string, List<ISuppGeneItem>>();
            using (_reader)
            {
                string line;
                while ((line = _reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    AddItem(line, geneAnnotations);
                }
            }

            if (geneAnnotations.Count == 0) throw new UserErrorException("The provided TSV has no valid custom annotation entries.");
            return geneAnnotations;
        }

        private void AddItem(string line, IDictionary<string, List<ISuppGeneItem>> geneAnnotations)
        {
            var splits = line.OptimizedSplit('\t');
            if (splits.Length != _tags.Length)
                throw new UserErrorException($"Column number mismatch!! Header has {_tags.Length} columns but {line} contains {splits.Length}");

            string geneId = splits[1];

            var annotationValues = new string[_numAnnotationColumns];
            var hasAnnotation = false;
            for (var i = 0; i < _numAnnotationColumns; i++)
            {
                string annotationValue = splits[i + NumRequiredColumns];
                if (annotationValue != "" && annotationValue != ".") hasAnnotation = true;

                annotationValues[i] = annotationValue;
                _annotationValidators[i](annotationValues[i], line);
            }

            if (!hasAnnotation) throw new UserErrorException($"No annotation provided in line {line}");

            string geneSymbol = GeneUtilities.GetGeneSymbolFromId(geneId, _entrezGeneIdToSymbol, _ensemblIdToSymbol);
            if (geneAnnotations.ContainsKey(geneSymbol)) throw new UserErrorException($"Found the same gene {geneSymbol} in different lines. Current line is: {line}");
            
            geneAnnotations[geneSymbol] = new List<ISuppGeneItem> {new CustomGene(geneSymbol, annotationValues.Select(x => new[] {x}).ToList(), JsonSchema, line)};
        }

        public void Dispose() => _reader?.Dispose();
    }
}