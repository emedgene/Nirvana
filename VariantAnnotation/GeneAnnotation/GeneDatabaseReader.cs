﻿using ErrorHandling.Exceptions;
using System;
using System.Collections.Generic;
using System.IO;
using VariantAnnotation.Interface.GeneAnnotation;
using VariantAnnotation.Interface.Providers;
using VariantAnnotation.Interface.Sequence;
using VariantAnnotation.IO;
using VariantAnnotation.Providers;
using VariantAnnotation.SA;

namespace VariantAnnotation.GeneAnnotation
{
    public sealed class GeneDatabaseReader
    {

        private readonly ExtendedBinaryReader _reader;
        private GenomeAssembly GenomeAssembly;
        private long _creationTime;
        public readonly List<IDataSourceVersion> DataSourceVersions;


        public GeneDatabaseReader(Stream geneDatabaseFileStream)
        {
            _reader = new ExtendedBinaryReader(geneDatabaseFileStream);
            DataSourceVersions = new List<IDataSourceVersion>();
            ReadHeader();
        }

        private void ReadHeader()
        {
            var header = _reader.ReadString();
            if (header != SaDataBaseCommon.DataHeader)
                throw new FormatException("Unrecognized header in this database");

            var dataVersion = _reader.ReadUInt16();
            

            var schema = _reader.ReadUInt16();
            if (schema != SaDataBaseCommon.SchemaVersion)
                throw new UserErrorException(
                    $"Gene database schema mismatch. Expected {SaDataBaseCommon.SchemaVersion}, observed {schema}");

            GenomeAssembly = (GenomeAssembly)_reader.ReadByte();

            _creationTime = _reader.ReadInt64();

            var dataSourseVersionsCount = _reader.ReadOptInt32();

            for (var i = 0; i < dataSourseVersionsCount; i++)
            {
                DataSourceVersions.Add(DataSourceVersion.Read(_reader));
            }

            CheckGuard();
        }

        public IEnumerable<IAnnotatedGene> Read()
        {

            IAnnotatedGene annotatedGene;
            while ((annotatedGene = AnnotatedGene.Read(_reader)) != null)
            {
                yield return annotatedGene;
            }
        }

        private void CheckGuard()
        {
            var observedGuard = _reader.ReadUInt32();
            if (observedGuard != SaDataBaseCommon.GuardInt)
            {
                throw new UserErrorException($"Expected a guard integer ({SaDataBaseCommon.GuardInt}), but found another value: ({observedGuard})");
            }
        }
    }
}
