BEGIN;

ALTER TABLE IF EXISTS public."Utilisateurs"
    ADD COLUMN IF NOT EXISTS "CodePays" character varying(2) COLLATE pg_catalog."default";

UPDATE public."Utilisateurs"
SET "CodePays" = UPPER("CodePays")
WHERE "CodePays" IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_Utilisateurs_CodePays'
    ) THEN
        ALTER TABLE public."Utilisateurs"
            ADD CONSTRAINT "CK_Utilisateurs_CodePays"
            CHECK ("CodePays" IS NULL OR "CodePays" ~ '^[A-Z]{2}$');
    END IF;
END $$;

COMMIT;
