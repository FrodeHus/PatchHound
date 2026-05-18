using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    /// <summary>
    /// Adds an IMMUTABLE SQL helper that mirrors <c>ExposureDerivationService.VersionMatches</c>.
    /// Required by the single-statement derivation+upsert pipeline so version-range filtering
    /// can run server-side instead of buffering the full pre-filter cross-join in the worker.
    /// </summary>
    public partial class AddVersionMatchesFunction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Semantics mirror System.Version.TryParse + the C# VersionMatches helper:
            //   - No predicates set         -> match
            //   - Installed unparseable     -> match (don't drop on parse failure)
            //   - Bound unparseable         -> ignore that bound
            //   - Otherwise                 -> compare dotted-numeric versions element-wise.
            // Parseable shape: 1..4 numeric components separated by '.', matching System.Version.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION patchhound_version_matches(
                    installed text,
                    start_inc text,
                    start_exc text,
                    end_inc   text,
                    end_exc   text)
                RETURNS boolean
                LANGUAGE plpgsql
                IMMUTABLE
                PARALLEL SAFE
                AS $fn$
                DECLARE
                    has_predicate boolean := (start_inc IS NOT NULL AND btrim(start_inc) <> '')
                                          OR (start_exc IS NOT NULL AND btrim(start_exc) <> '')
                                          OR (end_inc   IS NOT NULL AND btrim(end_inc)   <> '')
                                          OR (end_exc   IS NOT NULL AND btrim(end_exc)   <> '');
                    v int[];
                    b int[];
                BEGIN
                    IF NOT has_predicate THEN
                        RETURN true;
                    END IF;

                    IF installed IS NULL
                       OR btrim(installed) = ''
                       OR installed !~ '^[0-9]+(\.[0-9]+){1,3}$' THEN
                        RETURN true;
                    END IF;
                    v := string_to_array(installed, '.')::int[];

                    IF start_inc IS NOT NULL AND start_inc ~ '^[0-9]+(\.[0-9]+){1,3}$' THEN
                        b := string_to_array(start_inc, '.')::int[];
                        IF v < b THEN RETURN false; END IF;
                    END IF;

                    IF start_exc IS NOT NULL AND start_exc ~ '^[0-9]+(\.[0-9]+){1,3}$' THEN
                        b := string_to_array(start_exc, '.')::int[];
                        IF v <= b THEN RETURN false; END IF;
                    END IF;

                    IF end_inc IS NOT NULL AND end_inc ~ '^[0-9]+(\.[0-9]+){1,3}$' THEN
                        b := string_to_array(end_inc, '.')::int[];
                        IF v > b THEN RETURN false; END IF;
                    END IF;

                    IF end_exc IS NOT NULL AND end_exc ~ '^[0-9]+(\.[0-9]+){1,3}$' THEN
                        b := string_to_array(end_exc, '.')::int[];
                        IF v >= b THEN RETURN false; END IF;
                    END IF;

                    RETURN true;
                END;
                $fn$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS patchhound_version_matches(text, text, text, text, text);");
        }
    }
}
