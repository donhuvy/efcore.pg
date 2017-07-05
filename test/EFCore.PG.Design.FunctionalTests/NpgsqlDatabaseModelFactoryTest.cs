﻿using System.Collections.Generic;
using System.Linq;
using Xunit;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.FunctionalTests.Utilities;

namespace Npgsql.EntityFrameworkCore.PostgreSQL.Design.FunctionalTests
{
    public class NpgsqlDatabaseModelFactoryTest : IClassFixture<NpgsqlDatabaseModelFixture>
    {
        [Fact]
        public void It_reads_tables()
        {
            var sql = @"
CREATE TABLE public.everest (id int);
CREATE TABLE public.denali (id int);";
            var dbModel = CreateModel(sql, new List<string> { "everest", "denali" });

            Assert.Collection(dbModel.Tables.OrderBy(t => t.Name),
                d =>
                {
                    Assert.Equal("public", d.SchemaName);
                    Assert.Equal("denali", d.Name);
                },
                e =>
                {
                    Assert.Equal("public", e.SchemaName);
                    Assert.Equal("everest", e.Name);
                });
        }

        [Fact]
        public void It_reads_foreign_keys()
        {
            _fixture.ExecuteNonQuery("CREATE SCHEMA db2");
            var sql = "CREATE TABLE public.ranges (id INT PRIMARY KEY);" +
                      "CREATE TABLE db2.mountains (range_id INT NOT NULL, FOREIGN KEY (range_id) REFERENCES ranges(id) ON DELETE CASCADE)";
            var dbModel = CreateModel(sql, new List<string> { "ranges", "mountains" });

            var fk = Assert.Single(dbModel.Tables.Single(t => t.ForeignKeys.Count > 0).ForeignKeys);

            Assert.Equal("db2", fk.Table.SchemaName);
            Assert.Equal("mountains", fk.Table.Name);
            Assert.Equal("public", fk.PrincipalTable.SchemaName);
            Assert.Equal("ranges", fk.PrincipalTable.Name);
            Assert.Equal("range_id", fk.Columns.Single().Column.Name);
            Assert.Equal("id", fk.Columns.Single().PrincipalColumn.Name);
            Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
        }

        [Fact]
        public void It_reads_composite_foreign_keys()
        {
            _fixture.ExecuteNonQuery("CREATE SCHEMA db3");
            var sql = "CREATE TABLE public.ranges1 (id INT, alt_id INT, PRIMARY KEY(id, alt_id));" +
                      "CREATE TABLE db3.mountains1 (range_id INT NOT NULL, range_alt_id INT NOT NULL, FOREIGN KEY (range_id, range_alt_id) REFERENCES ranges1(id, alt_id) ON DELETE NO ACTION)";
            var dbModel = CreateModel(sql, new List<string> { "ranges1", "mountains1" });

            var fk = Assert.Single(dbModel.Tables.Single(t => t.ForeignKeys.Count > 0).ForeignKeys);

            Assert.Equal("db3", fk.Table.SchemaName);
            Assert.Equal("mountains1", fk.Table.Name);
            Assert.Equal("public", fk.PrincipalTable.SchemaName);
            Assert.Equal("ranges1", fk.PrincipalTable.Name);
            Assert.Equal(new[] { "range_id", "range_alt_id" }, fk.Columns.Select(c => c.Column.Name).ToArray());
            Assert.Equal(new[] { "id", "alt_id" }, fk.Columns.Select(c => c.PrincipalColumn.Name).ToArray());
            Assert.Equal(ReferentialAction.NoAction, fk.OnDelete);
        }

        [Fact]
        public void It_reads_indexes()
        {
            var sql = @"CREATE TABLE place (id int PRIMARY KEY, name int UNIQUE, location int);" +
                      @"CREATE INDEX ""IX_name_location"" ON place (name, location)";
            var dbModel = CreateModel(sql, new List<string> { "place" });

            var indexes = dbModel.Tables.Single().Indexes;

            Assert.All(indexes, c =>
            {
                Assert.Equal("public", c.Table.SchemaName);
                Assert.Equal("place", c.Table.Name);
            });

            Assert.Collection(indexes,
                unique =>
                {
                    Assert.True(unique.IsUnique);
                    Assert.Equal("name", unique.IndexColumns.Single().Column.Name);
                },
                composite =>
                {
                    Assert.Equal("IX_name_location", composite.Name);
                    Assert.False(composite.IsUnique);
                    Assert.Equal(new List<string> { "name", "location" }, composite.IndexColumns.Select(c => c.Column.Name).ToList());
                });
        }

        [Fact]
        public void It_reads_columns()
        {
            var sql = @"
CREATE TABLE public.mountains_columns (
    id int,
    name varchar(100) NOT NULL,
    latitude decimal(5,2) DEFAULT 0.0,
    created timestamp DEFAULT now(),
    discovered_date numeric,
    PRIMARY KEY (name, id)
);";
            var dbModel = CreateModel(sql, new List<string> { "mountains_columns" });

            var columns = dbModel.Tables.Single().Columns.OrderBy(c => c.Ordinal);

            Assert.All(columns, c =>
            {
                Assert.Equal("public", c.Table.SchemaName);
                Assert.Equal("mountains_columns", c.Table.Name);
            });

            Assert.Collection(
                columns,
                id =>
                    {
                        Assert.Equal("id", id.Name);
                        Assert.Equal("int4", id.StoreType);
                        Assert.Equal(2, id.PrimaryKeyOrdinal);
                        Assert.False(id.IsNullable);
                        Assert.Equal(0, id.Ordinal);
                        Assert.Null(id.DefaultValue);
                    },
                name =>
                    {
                        Assert.Equal("name", name.Name);
                        Assert.Equal("varchar(100)", name.StoreType);
                        Assert.Equal(1, name.PrimaryKeyOrdinal);
                        Assert.False(name.IsNullable);
                        Assert.Equal(1, name.Ordinal);
                        Assert.Null(name.DefaultValue);
                    },
                lat =>
                    {
                        Assert.Equal("latitude", lat.Name);
                        Assert.Equal("numeric(5, 2)", lat.StoreType);
                        Assert.Null(lat.PrimaryKeyOrdinal);
                        Assert.True(lat.IsNullable);
                        Assert.Equal(2, lat.Ordinal);
                        Assert.Equal("0.0", lat.DefaultValue);
                    },
                created =>
                    {
                        Assert.Equal("created", created.Name);
                        Assert.Equal("timestamp", created.StoreType);
                        Assert.Null(created.PrimaryKeyOrdinal);
                        Assert.True(created.IsNullable);
                        Assert.Equal(3, created.Ordinal);
                        Assert.Equal("now()", created.DefaultValue);
                    },
                discovered =>
                    {
                        Assert.Equal("discovered_date", discovered.Name);
                        Assert.Equal("numeric", discovered.StoreType);
                        Assert.Null(discovered.PrimaryKeyOrdinal);
                        Assert.True(discovered.IsNullable);
                        Assert.Equal(4, discovered.Ordinal);
                        Assert.Null(discovered.DefaultValue);
                    });
        }

        [Theory]
        [InlineData("varchar(341)", 341)]
        [InlineData("char(89)", 89)]
        public void It_reads_max_length(string type, int? length)
        {
            var sql = "DROP TABLE IF EXISTS strings;" +
                     $"CREATE TABLE public.strings (char_column {type});";
            var db = CreateModel(sql, new List<string> { "strings" });

            Assert.Equal(type, db.Tables.Single().Columns.Single().StoreType);
        }

        [Fact]
        public void It_reads_pk()
        {
            var dbModel = CreateModel(
                "CREATE TABLE pks (id int PRIMARY KEY, non_id int)",
                new List<string> { "pks" });

            var columns = dbModel.Tables.Single().Columns.OrderBy(c => c.Ordinal);
            Assert.Collection(columns,
                id =>
                {
                    Assert.Equal("id", id.Name);
                    Assert.Equal(1, id.PrimaryKeyOrdinal);
                },
                nonId =>
                {
                    Assert.Equal("non_id", nonId.Name);
                    Assert.Null(nonId.PrimaryKeyOrdinal);
                });
        }

        [Fact]
        public void It_filters_tables()
        {
            var sql = @"CREATE TABLE public.k2 (id int, a varchar, UNIQUE (a));" +
                      @"CREATE TABLE public.kilimanjaro (id int, b varchar, UNIQUE (b), FOREIGN KEY (b) REFERENCES k2 (a));";

            var selectionSet = new List<string> { "k2" };

            var dbModel = CreateModel(sql, selectionSet);
            var table = Assert.Single(dbModel.Tables);
            Assert.Equal("k2", table.Name);
            Assert.Equal(2, table.Columns.Count);
            Assert.Equal(1, table.Indexes.Count);
            Assert.Empty(table.ForeignKeys);
        }

        [Fact]
        public void It_reads_sequences()
        {
            var sql = @"CREATE SEQUENCE ""DefaultValues_ascending_read"";
 
CREATE SEQUENCE ""DefaultValues_descending_read"" INCREMENT BY -1;

CREATE SEQUENCE ""CustomSequence_read""
    START WITH 1 
    INCREMENT BY 2 
    MAXVALUE 8 
    MINVALUE -3 
    CYCLE;";

            var dbModel = CreateModel(sql);
            Assert.Collection(dbModel.Sequences.Where(s => s.Name.EndsWith("_read")).OrderBy(s => s.Name),
                c =>
                    {
                        Assert.Equal(c.Name, "CustomSequence_read");
                        Assert.Equal(c.SchemaName, "public");
                        Assert.Equal(c.DataType, "bigint");
                        Assert.Equal(1, c.Start);
                        Assert.Equal(2, c.IncrementBy);
                        Assert.Equal(8, c.Max);
                        Assert.Equal(-3, c.Min);
                        Assert.True(c.IsCyclic);
                    },
                da =>
                    {
                        Assert.Equal(da.Name, "DefaultValues_ascending_read");
                        Assert.Equal(da.SchemaName, "public");
                        Assert.Equal(da.DataType, "bigint");
                        Assert.Equal(1, da.IncrementBy);
                        Assert.False(da.IsCyclic);
                        Assert.Null(da.Max);
                        Assert.Null(da.Min);
                        Assert.Null(da.Start);
                    },
                dd =>
                {
                    Assert.Equal(dd.Name, "DefaultValues_descending_read");
                    Assert.Equal(dd.SchemaName, "public");
                    Assert.Equal(dd.DataType, "bigint");
                    Assert.Equal(-1, dd.IncrementBy);
                    Assert.False(dd.IsCyclic);
                    Assert.Null(dd.Max);
                    Assert.Null(dd.Min);
                    Assert.Null(dd.Start);
                });
        }

        [Fact]
        public void SequenceSerial()
        {
            var dbModel = CreateModel(@"CREATE TABLE ""SerialSequence"" (""SerialSequenceId"" serial primary key)");

            // Sequences which belong to a serial column should not get reverse engineered as separate sequences
            Assert.Empty(dbModel.Sequences.Where(s => s.Name == "SerialSequence_SerialSequenceId_seq"));

            // Now make sure the field itself is properly reverse-engineered.
            var column = dbModel.Tables.Single(t => t.Name == "SerialSequence").Columns.Single();
            Assert.Equal(ValueGenerated.OnAdd, column.ValueGenerated);
            Assert.Null(column.DefaultValue);
            //Assert.True(column.Npgsql().IsSerial);
        }

        [Fact]
        public void SequenceNonSerial()
        {
            var dbModel = CreateModel(@"
CREATE SEQUENCE some_sequence;
CREATE TABLE non_serial_sequence (id integer PRIMARY KEY DEFAULT nextval('some_sequence'))");

            var column = dbModel.Tables.Single(t => t.Name == "non_serial_sequence").Columns.Single();
            Assert.Equal("nextval('some_sequence'::regclass)", column.DefaultValue);
            // Npgsql has special identification for serial columns (scaffolding them with ValueGenerated.OnAdd
            // and removing the default), but not for non-serial sequence-driven columns, which are scaffolded
            // with a DefaultValue. This is consistent with the SqlServer scaffolding behavior.
            Assert.Null(column.ValueGenerated);
        }

        /*
        [Fact, IssueLink("https://github.com/npgsql/Npgsql.EntityFrameworkCore.PostgreSQL/issues/77")]
        public void SequencesOnlyFromRequestedSchema()
        {
            var dbModel = CreateModel(@"
CREATE SEQUENCE public.some_sequence2;
CREATE SCHEMA not_interested;
CREATE SEQUENCE not_interested.some_other_sequence;
", new TableSelectionSet(Enumerable.Empty<string>(), new[] { "public" }));

            var sequence = dbModel.Sequences.Single();
            Assert.Equal("public", sequence.SchemaName);
            Assert.Equal("some_sequence2", sequence.Name);
        }*/

        [Fact]
        public void DefaultSchemaIsPublic()
            => Assert.Equal("public", _fixture.CreateModel("SELECT 1").DefaultSchemaName);

        readonly NpgsqlDatabaseModelFixture _fixture;

        public DatabaseModel CreateModel(string createSql, IEnumerable<string> tables = null)
            => _fixture.CreateModel(createSql, tables);

        public NpgsqlDatabaseModelFactoryTest(NpgsqlDatabaseModelFixture fixture)
        {
            _fixture = fixture;
        }
    }
}