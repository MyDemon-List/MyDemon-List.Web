BEGIN;

ALTER TABLE IF EXISTS public."Utilisateurs"
    ADD COLUMN IF NOT EXISTS "LanguePreferee" character varying(2) COLLATE pg_catalog."default";

UPDATE public."Utilisateurs"
SET "LanguePreferee" = LOWER("LanguePreferee")
WHERE "LanguePreferee" IS NOT NULL;

UPDATE public."Utilisateurs"
SET "LanguePreferee" = NULL
WHERE "LanguePreferee" IS NOT NULL
  AND "LanguePreferee" NOT IN ('fr', 'en', 'es');

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'CK_Utilisateurs_LanguePreferee'
    ) THEN
        ALTER TABLE public."Utilisateurs"
            ADD CONSTRAINT "CK_Utilisateurs_LanguePreferee"
            CHECK ("LanguePreferee" IS NULL OR "LanguePreferee" IN ('fr', 'en', 'es'));
    END IF;
END $$;

COMMIT;
