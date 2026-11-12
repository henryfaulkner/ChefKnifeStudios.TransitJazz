SELECT now() AS "time", 'global'::text AS metric,
       CASE WHEN m.id IS NULL
                  OR m.watermark_utc IS NULL
                  OR m.watermark_utc < now() - interval '2 hours'
            THEN 1::double precision ELSE 0::double precision END AS value
FROM (VALUES (1)) AS expected(id)
LEFT JOIN client_experience_maintenance m ON m.id = expected.id
ORDER BY 1, 2;
