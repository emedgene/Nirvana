﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CacheUtils.TranscriptCache;
using VariantAnnotation.AnnotatedPositions.Transcript;
using VariantAnnotation.Caches;
using VariantAnnotation.Caches.DataStructures;
using VariantAnnotation.Interface.AnnotatedPositions;
using VariantAnnotation.Interface.Caches;
using VariantAnnotation.Interface.Intervals;
using VariantAnnotation.Interface.Sequence;
using VariantAnnotation.IO;
using VariantAnnotation.IO.Caches;
using VariantAnnotation.Sequence;
using Xunit;

namespace UnitTests.VariantAnnotation.IO.Caches
{
    public sealed class TranscriptCacheReaderTests
    {
        private readonly Dictionary<ushort, IChromosome> _refIndexToChromosome;
        private readonly TranscriptCacheData _expectedCacheData;
        private readonly CacheHeader _expectedHeader;

        public TranscriptCacheReaderTests()
        {
            var chr1 = new Chromosome("chr1", "1", 0);
            var chr2 = new Chromosome("chr2", "2", 1);
            var chr3 = new Chromosome("chr3", "3", 2);

            _refIndexToChromosome = new Dictionary<ushort, IChromosome>
            {
                [chr1.Index] = chr1,
                [chr2.Index] = chr2,
                [chr3.Index] = chr3
            };

            var genomeAssembly = GenomeAssembly.GRCh38;

            var customHeader = new TranscriptCacheCustomHeader(1, 2);
            _expectedHeader  = new CacheHeader("test", 2, 3, Source.BothRefSeqAndEnsembl, 4, genomeAssembly, customHeader);

            var introns = new IInterval[1];
            introns[0]  = new Interval(100, 200);

            var mirnas = new IInterval[2];
            mirnas[0]  = introns[0];
            mirnas[1]  = new Interval(300, 400);

            var peptideSeqs = new[] { "MASE*" };

            var genes = new IGene[1];
            genes[0]  = new Gene(chr3, 100, 200, true, "TP53", 300, CompactId.Convert("7157"),
                CompactId.Convert("ENSG00000141510"));

            var regulatoryRegions = new IRegulatoryRegion[2];
            regulatoryRegions[0]  = new RegulatoryRegion(chr3, 1200, 1300, CompactId.Convert("123"), RegulatoryRegionType.enhancer);
            regulatoryRegions[1]  = new RegulatoryRegion(chr3, 1250, 1450, CompactId.Convert("456"), RegulatoryRegionType.enhancer);
            var regulatoryRegionIntervalArrays = regulatoryRegions.ToIntervalArrays(3);

            var transcripts = GetTranscripts(chr3, genes, introns, mirnas);
            var transcriptIntervalArrays = transcripts.ToIntervalArrays(3);

            _expectedCacheData = new TranscriptCacheData(_expectedHeader, genes, introns, mirnas, peptideSeqs,
                transcriptIntervalArrays, regulatoryRegionIntervalArrays);
        }

        [Fact]
        public void TranscriptCacheReader_EndToEnd()
        {
            TranscriptCacheData observedCache;

            using (var ms = new MemoryStream())
            {
                using (var writer = new TranscriptCacheWriter(ms, _expectedHeader, true))
                {
                    writer.Write(_expectedCacheData);
                }

                ms.Position = 0;

                using (var reader = new TranscriptCacheReader(ms))
                {
                    observedCache = reader.Read(_refIndexToChromosome);
                }
            }

            Assert.NotNull(observedCache);
            Assert.Equal(_expectedCacheData.PeptideSeqs, observedCache.PeptideSeqs);
            CheckChromosomeIntervals(_expectedCacheData.Genes, observedCache.Genes);
            CheckIntervalArrays(_expectedCacheData.RegulatoryRegionIntervalArrays, observedCache.RegulatoryRegionIntervalArrays);
            CheckIntervalArrays(_expectedCacheData.TranscriptIntervalArrays, observedCache.TranscriptIntervalArrays);
            CheckIntervals(_expectedCacheData.Introns, observedCache.Introns);
            CheckIntervals(_expectedCacheData.Mirnas, observedCache.Mirnas);
        }

        private static void CheckIntervalArrays<T>(IntervalArray<T>[] expected, IntervalArray<T>[] observed)
            where T : IInterval
        {
            Assert.Equal(expected.Length, observed.Length);

            for (int refIndex = 0; refIndex < expected.Length; refIndex++)
            {
                var expectedIntervalArray = expected[refIndex];
                var observedIntervalArray = observed[refIndex];

                if (expectedIntervalArray == null && observedIntervalArray == null) continue;

                Assert.NotNull(expectedIntervalArray);
                Assert.NotNull(observedIntervalArray);
                Assert.Equal(expectedIntervalArray.Array.Length, observedIntervalArray.Array.Length);

                for (int i = 0; i < expectedIntervalArray.Array.Length; i++)
                {
                    var expectedInterval = expectedIntervalArray.Array[i];
                    var observedInterval = observedIntervalArray.Array[i];
                    Assert.Equal(expectedInterval.Begin, observedInterval.Begin);
                    Assert.Equal(expectedInterval.End, observedInterval.End);
                }
            }
        }

        private static void CheckChromosomeIntervals(IEnumerable<IChromosomeInterval> expected,
            IEnumerable<IChromosomeInterval> observed)
        {
            var expectedList = expected.ToList();
            var observedList = observed.ToList();

            Assert.Equal(expectedList.Count, observedList.Count);

            for (int i = 0; i < expectedList.Count; i++)
            {
                var expectedEntry = expectedList[i];
                var observedEntry = observedList[i];
                Assert.Equal(expectedEntry.Chromosome.EnsemblName, observedEntry.Chromosome.EnsemblName);
                Assert.Equal(expectedEntry.Start,                  observedEntry.Start);
                Assert.Equal(expectedEntry.End,                    observedEntry.End);
            }
        }

        private static void CheckIntervals(IEnumerable<IInterval> expected, IEnumerable<IInterval> observed)
        {
            var expectedList = expected.ToList();
            var observedList = observed.ToList();

            Assert.Equal(expectedList.Count, observedList.Count);

            for (int i = 0; i < expectedList.Count; i++)
            {
                var expectedEntry = expectedList[i];
                var observedEntry = observedList[i];
                Assert.Equal(expectedEntry.Start, observedEntry.Start);
                Assert.Equal(expectedEntry.End, observedEntry.End);
            }
        }

        [Fact]
        public void ReadItems_EndToEnd()
        {
            var expectedStrings = new[] { "Huey", "Duey", "Louie" };
            string[] observedStrings;

            using (var ms = new MemoryStream())
            {
                // ReSharper disable AccessToDisposedClosure
                using (var writer = new ExtendedBinaryWriter(ms, Encoding.UTF8, true))
                {
                    TranscriptCacheWriter.WriteItems(writer, expectedStrings, x => writer.WriteOptAscii(x));
                }

                ms.Position = 0;

                using (var reader = new ExtendedBinaryReader(ms))
                {
                    observedStrings = TranscriptCacheReader.ReadItems(reader, () => reader.ReadAsciiString());
                }
                // ReSharper restore AccessToDisposedClosure
            }

            Assert.NotNull(observedStrings);
            Assert.Equal(expectedStrings, observedStrings);
        }

        [Fact]
        public void CheckGuard_InvalidGuard()
        {
            Assert.Throws<InvalidDataException>(delegate
            {
                using (var ms = new MemoryStream())
                {
                    using (var writer = new ExtendedBinaryWriter(ms, Encoding.UTF8, true)) writer.Write(7);
                    ms.Position = 0;
                    using (var reader = new ExtendedBinaryReader(ms)) TranscriptCacheReader.CheckGuard(reader);
                }
            });
        }

        private static ITranscript[] GetTranscripts(IChromosome chromosome, IGene[] genes, IInterval[] introns, IInterval[] mirnas)
        {
            var cdnaMaps = new ICdnaCoordinateMap[1];
            cdnaMaps[0]  = new CdnaCoordinateMap(100, 199, 300, 399);

            var transcripts = new ITranscript[1];
            transcripts[0]  = new Transcript(chromosome, 120, 180, CompactId.Convert("789"), null, BioType.IG_D_gene,
                genes[0], 0, 0, false, introns, mirnas, cdnaMaps, -1, -1, Source.None, false, false, null, null);

            return transcripts;
        }
    }
}
