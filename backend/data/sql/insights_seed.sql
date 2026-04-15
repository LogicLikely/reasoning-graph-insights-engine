--
-- PostgreSQL database dump
--

-- Dumped from database version 17.4
-- Dumped by pg_dump version 17.4

-- Started on 2026-04-14 19:10:26 EDT

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 220 (class 1259 OID 16667)
-- Name: edges; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.edges (
    id text NOT NULL,
    graph_id integer NOT NULL,
    from_node_id text NOT NULL,
    to_node_id text NOT NULL,
    kind text NOT NULL,
    weight numeric(6,5),
    confidence numeric(6,5),
    polarity smallint,
    rationale text,
    attributes jsonb DEFAULT '{}'::jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_edges_confidence CHECK (((confidence IS NULL) OR ((confidence >= (0)::numeric) AND (confidence <= (1)::numeric)))),
    CONSTRAINT ck_edges_kind CHECK ((kind = ANY (ARRAY['support'::text, 'rebut'::text]))),
    CONSTRAINT ck_edges_not_self CHECK ((from_node_id <> to_node_id)),
    CONSTRAINT ck_edges_polarity CHECK (((polarity IS NULL) OR (polarity = ANY (ARRAY['-1'::integer, 1])))),
    CONSTRAINT ck_edges_weight CHECK (((weight IS NULL) OR ((weight >= (0)::numeric) AND (weight <= (1)::numeric))))
);


ALTER TABLE public.edges OWNER TO postgres;

--
-- TOC entry 218 (class 1259 OID 16635)
-- Name: graphs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.graphs (
    id integer NOT NULL,
    slug text NOT NULL,
    title text NOT NULL,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.graphs OWNER TO postgres;

--
-- TOC entry 217 (class 1259 OID 16634)
-- Name: graphs_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.graphs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.graphs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 219 (class 1259 OID 16646)
-- Name: nodes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.nodes (
    id text NOT NULL,
    graph_id integer NOT NULL,
    kind text NOT NULL,
    title text NOT NULL,
    body_text text NOT NULL,
    category text,
    tags text[] DEFAULT '{}'::text[] NOT NULL,
    prior numeric(6,5),
    weight numeric(6,5),
    confidence numeric(6,5),
    importance numeric(6,5),
    evidence jsonb,
    attributes jsonb DEFAULT '{}'::jsonb NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_nodes_confidence CHECK (((confidence IS NULL) OR ((confidence >= (0)::numeric) AND (confidence <= (1)::numeric)))),
    CONSTRAINT ck_nodes_importance CHECK (((importance IS NULL) OR ((importance >= (0)::numeric) AND (importance <= (1)::numeric)))),
    CONSTRAINT ck_nodes_kind CHECK ((kind = ANY (ARRAY['root'::text, 'claim'::text, 'premise'::text, 'counter'::text, 'evidence'::text]))),
    CONSTRAINT ck_nodes_prior CHECK (((prior IS NULL) OR ((prior >= (0)::numeric) AND (prior <= (1)::numeric)))),
    CONSTRAINT ck_nodes_weight CHECK (((weight IS NULL) OR ((weight >= (0)::numeric) AND (weight <= (1)::numeric))))
);


ALTER TABLE public.nodes OWNER TO postgres;

--
-- TOC entry 3647 (class 0 OID 16667)
-- Dependencies: 220
-- Data for Name: edges; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.edges VALUES ('E-R-C1', 1, 'C1', 'R1', 'support', 0.23000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-R-C2', 1, 'C2', 'R1', 'support', 0.21000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-R-C3', 1, 'C3', 'R1', 'support', 0.18000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-R-C4', 1, 'C4', 'R1', 'support', 0.23000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-R-C5', 1, 'C5', 'R1', 'support', 0.15000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C1-P1', 1, 'P1', 'C1', 'support', 0.60000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C1-E1', 1, 'E1', 'C1', 'support', 0.40000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C2-P2', 1, 'P2', 'C2', 'support', 0.55000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C2-E2', 1, 'E2', 'C2', 'support', 0.45000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C3-P3', 1, 'P3', 'C3', 'support', 0.50000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C3-P4', 1, 'P4', 'C3', 'support', 0.25000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C3-E3', 1, 'E3', 'C3', 'support', 0.15000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C3-E4', 1, 'E4', 'C3', 'support', 0.10000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C4-P5', 1, 'P5', 'C4', 'support', 0.55000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C4-P6', 1, 'P6', 'C4', 'support', 0.20000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C4-E5', 1, 'E5', 'C4', 'support', 0.15000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C4-E6', 1, 'E6', 'C4', 'support', 0.10000, NULL, 1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C5-O8', 1, 'O8', 'C5', 'rebut', 0.80000, NULL, -1, 'Challenges conspiracy-style support branch.', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O1-P1', 1, 'O1', 'P1', 'rebut', 0.75000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O1-C1', 1, 'O1', 'C1', 'rebut', 0.25000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O2-P5', 1, 'O2', 'P5', 'rebut', 0.65000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O2-P6', 1, 'O2', 'P6', 'rebut', 0.35000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O3-P2', 1, 'O3', 'P2', 'rebut', 0.80000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O4-E3', 1, 'O4', 'E3', 'rebut', 0.55000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O4-E4', 1, 'O4', 'E4', 'rebut', 0.45000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O5-P4', 1, 'O5', 'P4', 'rebut', 0.45000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O5-E4', 1, 'O5', 'E4', 'rebut', 0.55000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O6-P5', 1, 'O6', 'P5', 'rebut', 0.70000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O6-C4', 1, 'O6', 'C4', 'rebut', 0.30000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O7-P6', 1, 'O7', 'P6', 'rebut', 0.55000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O7-E6', 1, 'O7', 'E6', 'rebut', 0.45000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O9-C1', 1, 'O9', 'C1', 'rebut', 0.34000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O9-C3', 1, 'O9', 'C3', 'rebut', 0.33000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O9-C4', 1, 'O9', 'C4', 'rebut', 0.33000, NULL, -1, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');


--
-- TOC entry 3645 (class 0 OID 16635)
-- Dependencies: 218
-- Data for Name: graphs; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.graphs OVERRIDING SYSTEM VALUE VALUES (1, 'sample-medium', 'Sample Medium Reasoning Graph', 'Seed graph based on the sample TypeScript dataset.', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');


--
-- TOC entry 3646 (class 0 OID 16646)
-- Dependencies: 219
-- Data for Name: nodes; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.nodes VALUES ('R1', 1, 'root', 'Earth is flat', 'The Earth is flat.', NULL, '{flat-earth,root}', 0.20000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C1', 1, 'claim', 'The horizon looks flat', 'The horizon appears flat to everyday observation.', 'observation', '{}', 0.55000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C2', 1, 'claim', 'Water finds level', 'Water seeks its level and should not conform to a sphere.', 'physical-intuition', '{}', 0.50000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C3', 1, 'claim', 'No obvious curvature from altitude', 'Images from balloons and planes do not show obvious curvature.', 'visual-observation', '{}', 0.45000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C4', 1, 'claim', 'Long-distance visibility seems too great', 'Distant objects are visible farther away than expected on a globe.', 'distance-claims', '{}', 0.48000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C5', 1, 'claim', 'Mainstream institutions coordinate the narrative', 'Agencies and institutions reinforce a globe narrative that should be questioned.', 'meta-claim', '{}', 0.30000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P1', 1, 'premise', 'Beach and ocean horizons appear straight', 'At sea level, the horizon usually appears flat and level.', 'observation', '{}', 0.68000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P2', 1, 'premise', 'Canals and lakes look level', 'Large bodies of water appear level over long distances.', 'observation', '{}', 0.64000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P3', 1, 'premise', 'Airplane windows do not show obvious curve', 'Commercial flights rarely present a visibly curved horizon to passengers.', 'visual-observation', '{}', 0.60000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P4', 1, 'premise', 'Balloon footage can look flat', 'Some high-altitude footage appears to show a flat horizon.', 'visual-observation', '{}', 0.58000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P5', 1, 'premise', 'Distant skylines remain visible', 'Some city skylines or mountains appear visible from surprising distances.', 'distance-claims', '{}', 0.62000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P6', 1, 'premise', 'Laser and line-of-sight tests look level', 'Some amateur tests report level measurements over water.', 'measurement', '{}', 0.57000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E1', 1, 'evidence', 'Photographs from beaches', 'Collections of beach and ocean photographs are cited as visual support.', NULL, '{}', 0.52000, NULL, NULL, NULL, '{"type": "observational", "score": 0.52, "rationale": "Visual examples are easy to gather but hard to interpret precisely."}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E2', 1, 'evidence', 'Canal observations', 'Flat-earth arguments often cite calm water surfaces and canal observations.', NULL, '{}', 0.50000, NULL, NULL, NULL, '{"type": "observational", "score": 0.5}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E3', 1, 'evidence', 'Passenger flight photos', 'Passenger photos and videos from aircraft are used as support.', NULL, '{}', 0.46000, NULL, NULL, NULL, '{"type": "video", "score": 0.46}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E4', 1, 'evidence', 'Balloon video clips', 'Selected balloon footage is cited as evidence that curvature is not obvious.', NULL, '{}', 0.47000, NULL, NULL, NULL, '{"type": "video", "score": 0.47}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E5', 1, 'evidence', 'Long-distance photo examples', 'Photos of distant buildings and mountains are used to argue visibility is too great.', NULL, '{}', 0.49000, NULL, NULL, NULL, '{"type": "observational", "score": 0.49}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E6', 1, 'evidence', 'Amateur measurement experiments', 'Laser tests, zoom tests, and line-of-sight measurements are cited in support.', NULL, '{}', 0.44000, NULL, NULL, NULL, '{"type": "experimental", "score": 0.44}', '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O1', 1, 'counter', 'Human perception is a poor curvature detector', 'At normal scales, human vision is not a reliable way to detect Earth''s curvature.', 'visual-limit', '{}', 0.82000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O2', 1, 'counter', 'Atmospheric refraction affects visibility', 'Refraction can make distant objects appear higher or more visible than expected.', 'optics', '{}', 0.84000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O3', 1, 'counter', 'Water level locally does not imply global flatness', 'A surface can be locally level while still conforming to a sphere.', 'geometry', '{}', 0.86000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O4', 1, 'counter', 'Camera lenses can distort curvature', 'Lens choice, framing, and post-processing can affect whether curvature appears visible.', 'imaging', '{}', 0.72000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O5', 1, 'counter', 'Balloon footage is not a controlled measurement', 'Anecdotal video clips are weaker than calibrated measurement.', 'methodology', '{}', 0.77000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O6', 1, 'counter', 'Distance claims depend on elevation and hidden-height math', 'Visibility calculations depend on observer height, object height, and atmospheric conditions.', 'geometry', '{}', 0.81000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O7', 1, 'counter', 'Amateur tests often lack controls', 'DIY experiments frequently omit calibration, repeatability, and environmental controls.', 'methodology', '{}', 0.79000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O8', 1, 'counter', 'Institutional agreement is not by itself evidence of conspiracy', 'Broad agreement can reflect converging evidence rather than coordinated deception.', 'meta-claim', '{}', 0.74000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('O9', 1, 'counter', 'Multiple support branches rely on the same observational weakness', 'Several flat-earth arguments share the same limitations of casual observation.', 'cross-cutting', '{}', 0.76000, NULL, NULL, NULL, NULL, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');


--
-- TOC entry 3653 (class 0 OID 0)
-- Dependencies: 217
-- Name: graphs_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.graphs_id_seq', 1, true);


--
-- TOC entry 3489 (class 2606 OID 16681)
-- Name: edges edges_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_pkey PRIMARY KEY (id);


--
-- TOC entry 3478 (class 2606 OID 16643)
-- Name: graphs graphs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.graphs
    ADD CONSTRAINT graphs_pkey PRIMARY KEY (id);


--
-- TOC entry 3480 (class 2606 OID 16645)
-- Name: graphs graphs_slug_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.graphs
    ADD CONSTRAINT graphs_slug_key UNIQUE (slug);


--
-- TOC entry 3487 (class 2606 OID 16661)
-- Name: nodes nodes_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.nodes
    ADD CONSTRAINT nodes_pkey PRIMARY KEY (id);


--
-- TOC entry 3490 (class 1259 OID 16706)
-- Name: ix_edges_attributes_gin; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_attributes_gin ON public.edges USING gin (attributes);


--
-- TOC entry 3491 (class 1259 OID 16701)
-- Name: ix_edges_from_node_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_from_node_id ON public.edges USING btree (from_node_id);


--
-- TOC entry 3492 (class 1259 OID 16700)
-- Name: ix_edges_graph_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_graph_id ON public.edges USING btree (graph_id);


--
-- TOC entry 3493 (class 1259 OID 16703)
-- Name: ix_edges_kind; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_kind ON public.edges USING btree (kind);


--
-- TOC entry 3494 (class 1259 OID 16702)
-- Name: ix_edges_to_node_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_to_node_id ON public.edges USING btree (to_node_id);


--
-- TOC entry 3481 (class 1259 OID 16705)
-- Name: ix_nodes_attributes_gin; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_attributes_gin ON public.nodes USING gin (attributes);


--
-- TOC entry 3482 (class 1259 OID 16699)
-- Name: ix_nodes_category; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_category ON public.nodes USING btree (category);


--
-- TOC entry 3483 (class 1259 OID 16704)
-- Name: ix_nodes_evidence_gin; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_evidence_gin ON public.nodes USING gin (evidence);


--
-- TOC entry 3484 (class 1259 OID 16697)
-- Name: ix_nodes_graph_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_graph_id ON public.nodes USING btree (graph_id);


--
-- TOC entry 3485 (class 1259 OID 16698)
-- Name: ix_nodes_kind; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_kind ON public.nodes USING btree (kind);


--
-- TOC entry 3496 (class 2606 OID 16687)
-- Name: edges edges_from_node_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_from_node_id_fkey FOREIGN KEY (from_node_id) REFERENCES public.nodes(id) ON DELETE CASCADE;


--
-- TOC entry 3497 (class 2606 OID 16682)
-- Name: edges edges_graph_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_graph_id_fkey FOREIGN KEY (graph_id) REFERENCES public.graphs(id) ON DELETE CASCADE;


--
-- TOC entry 3498 (class 2606 OID 16692)
-- Name: edges edges_to_node_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_to_node_id_fkey FOREIGN KEY (to_node_id) REFERENCES public.nodes(id) ON DELETE CASCADE;


--
-- TOC entry 3495 (class 2606 OID 16662)
-- Name: nodes nodes_graph_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.nodes
    ADD CONSTRAINT nodes_graph_id_fkey FOREIGN KEY (graph_id) REFERENCES public.graphs(id) ON DELETE CASCADE;


-- Completed on 2026-04-14 19:10:26 EDT

--
-- PostgreSQL database dump complete
--

