using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PatchHound.Infrastructure.Data;

#nullable disable

namespace PatchHound.Infrastructure.Migrations
{
    [DbContext(typeof(PatchHoundDbContext))]
    [Migration("20260519120000_AddExactVersionStringMatch")]
    public partial class AddExactVersionStringMatch : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    has_exact_inclusive_bounds boolean := start_inc IS NOT NULL
                                                       AND btrim(start_inc) <> ''
                                                       AND end_inc IS NOT NULL
                                                       AND btrim(end_inc) <> ''
                                                       AND lower(btrim(start_inc)) = lower(btrim(end_inc))
                                                       AND (start_exc IS NULL OR btrim(start_exc) = '')
                                                       AND (end_exc IS NULL OR btrim(end_exc) = '');
                    v int[];
                    b int[];
                BEGIN
                    IF NOT has_predicate THEN
                        RETURN true;
                    END IF;

                    IF has_exact_inclusive_bounds
                       AND (installed IS NULL
                            OR btrim(installed) = ''
                            OR installed !~ '^[0-9]+(\.[0-9]+){1,3}$'
                            OR start_inc !~ '^[0-9]+(\.[0-9]+){1,3}$') THEN
                        RETURN installed IS NOT NULL
                            AND lower(btrim(installed)) = lower(btrim(start_inc));
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
