-- Données d'exemple pour le développement local uniquement (extraites d'un export réel, sans les infos sensibles :
-- pas de comptes Discord, pas de fusions d'utilisateurs).
-- Montées uniquement par docker-compose.yml (jamais par docker-compose.prod.yml).
BEGIN;

INSERT INTO public."Utilisateurs" ("Id", "Nom") VALUES
    (1, 'Sao'),
    (2, 'Zylenox'),
    (3, 'Manix648'),
    (4, 'RicoLP'),
    (5, 'LazerBlitz'),
    (6, 'Panman'),
    (7, 'xTet'),
    (8, 'HelpegasuS'),
    (9, 'xander'),
    (10, 'Wespdx'),
    (11, 'ZrKiphal'),
    (12, 'LChaseR'),
    (13, 'Kanati');
SELECT pg_catalog.setval('public."Utilisateurs_Id_seq"', 13, true);

INSERT INTO public."Listes" ("Id", "Nom", "UtilisateurId") VALUES
    (1, 'Sao''s List', 1);
SELECT pg_catalog.setval('public."Listes_Id_seq"', 1, true);

INSERT INTO public."Niveaux" ("Id", "IdDuNiveauDansLeJeu", "Nom", "UrlVerification", "Duree", "VerifieurId", "PublisherId", "RatingId", "ListeId") VALUES
    (1, '56460850', 'Triple Six', 'https://www.youtube.com/watch?v=VLt8A5E3Fz4', 0, 2, 2, 52, 1),
    (2, '35448603', 'Blade of Justice', 'https://www.youtube.com/watch?v=6018CbEjA-4', 0, 4, 3, 53, 1),
    (3, '129343574', 'AUTONOMICA', 'https://www.youtube.com/watch?v=c2aRyO_uaQ4', 0, 7, 7, 53, 1),
    (4, '112309508', 'Junk Realm', 'https://www.youtube.com/watch?v=_qjFyW9-cYU', 0, 9, 8, 54, 1);
SELECT pg_catalog.setval('public."Niveaux_Id_seq"', 4, true);

INSERT INTO public."Classements" ("Id", "ClassementPosition", "Points", "NiveauId", "ListeId") VALUES
    (1, 3, 667, 1, 1),
    (2, 1, 1000, 2, 1),
    (3, 2, 833, 3, 1),
    (4, 4, 500, 4, 1);
SELECT pg_catalog.setval('public."Classements_Id_seq"', 4, true);

INSERT INTO public."CreateursNiveaux" ("CreateurId", "NiveauId") VALUES
    (2, 1),
    (3, 2),
    (5, 2),
    (6, 2),
    (7, 3),
    (8, 4),
    (10, 4),
    (11, 4),
    (12, 4),
    (13, 4);

END;
