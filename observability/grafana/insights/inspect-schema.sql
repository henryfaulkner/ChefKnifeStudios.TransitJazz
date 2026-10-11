-- Run with an already authorized credential, via query.ps1 (read-only transaction).
-- Catalog-only discovery works even when the planned tables do not exist.
SELECT current_database() AS database_name, current_user AS database_role,
       current_setting('transaction_read_only') AS transaction_read_only,
       current_setting('transaction_isolation') AS transaction_isolation,
       current_setting('server_version') AS server_version;

SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolreplication, rolbypassrls
FROM pg_roles
WHERE rolname = current_user OR pg_has_role(oid, 'USAGE')
ORDER BY rolname;

SELECT to_regclass('public.city_category_minute_statistics') AS minute_table,
       to_regclass('public.city_category_hour_statistics') AS hour_table;

SELECT table_name, ordinal_position, column_name, data_type, is_nullable,
       character_maximum_length, numeric_precision, numeric_scale
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name IN ('city_category_minute_statistics', 'city_category_hour_statistics')
ORDER BY table_name, ordinal_position;

SELECT n.nspname AS schema_name, c.relname AS relation_name,
       has_table_privilege(c.oid, 'SELECT') AS can_select,
       has_table_privilege(c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER') AS has_elevated_table_privilege
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
  AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp%'
  AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
  AND (has_table_privilege(c.oid, 'SELECT')
       OR has_table_privilege(c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'))
ORDER BY schema_name, relation_name LIMIT 100;

SELECT conrelid::regclass AS relation_name, conname, contype,
       pg_get_constraintdef(oid) AS constraint_definition
FROM pg_constraint
WHERE conrelid IN (to_regclass('public.city_category_minute_statistics'),
                  to_regclass('public.city_category_hour_statistics'))
ORDER BY relation_name, conname;
