--
-- PostgreSQL database dump
--

-- Dumped from database version 17.4
-- Dumped by pg_dump version 18.2

-- Started on 2026-07-05 12:23:04 EDT

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
-- TOC entry 217 (class 1259 OID 18051)
-- Name: edges; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.edges (
    id text NOT NULL,
    graph_id integer NOT NULL,
    from_node_id text NOT NULL,
    to_node_id text NOT NULL,
    kind text NOT NULL,
    importance_to_parent numeric(10,3) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_edges_importance_to_parent CHECK (((importance_to_parent > 0) AND (importance_to_parent <= 10))),
    CONSTRAINT ck_edges_kind CHECK ((kind = ANY (ARRAY['support'::text, 'rebut'::text]))),
    CONSTRAINT ck_edges_not_self CHECK ((from_node_id <> to_node_id))
);


ALTER TABLE public.edges OWNER TO postgres;

--
-- TOC entry 218 (class 1259 OID 18061)
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
-- TOC entry 219 (class 1259 OID 18068)
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
-- TOC entry 220 (class 1259 OID 18069)
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
    log_odds double precision DEFAULT 0 NOT NULL,
    evidence jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT ck_nodes_kind CHECK ((kind = ANY (ARRAY['root'::text, 'claim'::text, 'objection'::text, 'evidence'::text]))),
    CONSTRAINT ck_nodes_log_odds_range CHECK (((log_odds >= ('-100'::integer)::double precision) AND (log_odds <= (100)::double precision)))
);


ALTER TABLE public.nodes OWNER TO postgres;

--
-- TOC entry 3636 (class 0 OID 18051)
-- Dependencies: 217
-- Data for Name: edges; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.edges VALUES ('E-C3-P3', 1, 'P3', 'C3', 'support', 1, '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-C3-E4', 1, 'E4', 'C3', 'support', 1, '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-O1-P1', 1, 'O1', 'P1', 'rebut', 1, '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.edges VALUES ('E-R-C4', 1, 'C4', 'R1', 'support', 7, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:01:29.131858-04');
INSERT INTO public.edges VALUES ('E-R-C5', 1, 'C5', 'R1', 'support', 3, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:01:54.039602-04');
INSERT INTO public.edges VALUES ('E-R-C3', 1, 'C3', 'R1', 'support', 2, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:02:34.463307-04');
INSERT INTO public.edges VALUES ('E-R-C2', 1, 'C2', 'R1', 'support', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:02:52.478407-04');
INSERT INTO public.edges VALUES ('E-R-C1', 1, 'C1', 'R1', 'support', 5, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:22.744863-04');
INSERT INTO public.edges VALUES ('E-C1-E1', 1, 'E1', 'C1', 'support', 3, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:43.314014-04');
INSERT INTO public.edges VALUES ('E-C1-P1', 1, 'P1', 'C1', 'support', 2, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:59.486207-04');
INSERT INTO public.edges VALUES ('E-O1-C1', 1, 'O1', 'C1', 'rebut', 10, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:04:16.680376-04');
INSERT INTO public.edges VALUES ('E-C2-E2', 1, 'E2', 'C2', 'support', 10, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:04:47.567154-04');
INSERT INTO public.edges VALUES ('E-C2-P2', 1, 'P2', 'C2', 'support', 8, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:05:05.810578-04');
INSERT INTO public.edges VALUES ('E-O3-P2', 1, 'O3', 'P2', 'rebut', 10, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:06:39.992305-04');
INSERT INTO public.edges VALUES ('E-C3-E3', 1, 'E3', 'C3', 'support', 3, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:07:14.505983-04');
INSERT INTO public.edges VALUES ('E-O4-E3', 1, 'O4', 'E3', 'rebut', 8, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:07:37.892759-04');
INSERT INTO public.edges VALUES ('E-O4-E4', 1, 'O4', 'E4', 'rebut', 8, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:07:37.903067-04');
INSERT INTO public.edges VALUES ('E-C3-P4', 1, 'P4', 'C3', 'support', 3, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:08:07.898937-04');
INSERT INTO public.edges VALUES ('E-O5-P4', 1, 'O5', 'P4', 'rebut', 7, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:08:45.158857-04');
INSERT INTO public.edges VALUES ('E-O5-E4', 1, 'O5', 'E4', 'rebut', 7, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:08:45.159191-04');
INSERT INTO public.edges VALUES ('E-O9-C1', 1, 'O9', 'C1', 'rebut', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:13.144113-04');
INSERT INTO public.edges VALUES ('E-O9-C3', 1, 'O9', 'C3', 'rebut', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:13.144171-04');
INSERT INTO public.edges VALUES ('E-O9-C4', 1, 'O9', 'C4', 'rebut', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:13.15444-04');
INSERT INTO public.edges VALUES ('E-C4-E5', 1, 'E5', 'C4', 'support', 6, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:45.312111-04');
INSERT INTO public.edges VALUES ('E-C4-E6', 1, 'E6', 'C4', 'support', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:04.425036-04');
INSERT INTO public.edges VALUES ('E-C4-P5', 1, 'P5', 'C4', 'support', 7, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:18.391606-04');
INSERT INTO public.edges VALUES ('E-C4-P6', 1, 'P6', 'C4', 'support', 2, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:53.475264-04');
INSERT INTO public.edges VALUES ('E-C5-O8', 1, 'O8', 'C5', 'rebut', 8, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:11:25.938129-04');
INSERT INTO public.edges VALUES ('E-O2-P5', 1, 'O2', 'P5', 'rebut', 5, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:11:48.19083-04');
INSERT INTO public.edges VALUES ('E-O2-P6', 1, 'O2', 'P6', 'rebut', 5, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:11:48.192292-04');
INSERT INTO public.edges VALUES ('E-O7-P6', 1, 'O7', 'P6', 'rebut', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:02.593273-04');
INSERT INTO public.edges VALUES ('E-O7-E6', 1, 'O7', 'E6', 'rebut', 9, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:02.593272-04');
INSERT INTO public.edges VALUES ('E-O6-P5', 1, 'O6', 'P5', 'rebut', 6, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:30.934387-04');
INSERT INTO public.edges VALUES ('E-O6-C4', 1, 'O6', 'C4', 'rebut', 6, '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:30.939403-04');


--
-- TOC entry 3637 (class 0 OID 18061)
-- Dependencies: 218
-- Data for Name: graphs; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.graphs OVERRIDING SYSTEM VALUE VALUES (1, 'sample-medium', 'Sample Medium Reasoning Graph', 'Seed graph based on the sample TypeScript dataset.', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');


--
-- TOC entry 3639 (class 0 OID 18069)
-- Dependencies: 220
-- Data for Name: nodes; Type: TABLE DATA; Schema: public; Owner: postgres
--

INSERT INTO public.nodes VALUES ('R1', 1, 'root', 'Earth is flat', 'The Earth is flat.', NULL, '{flat-earth,root}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('P3', 1, 'claim', 'Airplane windows do not show obvious curve', 'Commercial flights rarely present a visibly curved horizon to passengers.', 'visual-observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('E4', 1, 'evidence', 'Balloon video clips', 'Selected balloon footage is cited as evidence that curvature is not obvious.', NULL, '{}', 0, '{"type": "video", "score": 0.5}', '2026-04-13 11:42:53.694065-04', '2026-04-13 11:42:53.694065-04');
INSERT INTO public.nodes VALUES ('C4', 1, 'claim', 'Long-distance visibility seems too great', 'Distant objects are visible farther away than expected on a globe.', 'distance-claims', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:01:29.123906-04');
INSERT INTO public.nodes VALUES ('C5', 1, 'claim', 'Mainstream institutions coordinate the narrative', 'Agencies and institutions reinforce a globe narrative that should be questioned.', 'meta-claim', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:01:54.030317-04');
INSERT INTO public.nodes VALUES ('C3', 1, 'claim', 'No obvious curvature from altitude', 'Images from balloons and planes do not show obvious curvature.', 'visual-observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:02:34.463307-04');
INSERT INTO public.nodes VALUES ('C2', 1, 'claim', 'Water finds level', 'Water seeks its level and should not conform to a sphere.', 'physical-intuition', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:02:52.478407-04');
INSERT INTO public.nodes VALUES ('C1', 1, 'claim', 'The horizon looks flat', 'The horizon appears flat to everyday observation.', 'observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:22.744861-04');
INSERT INTO public.nodes VALUES ('E1', 1, 'evidence', 'Photographs from beaches', 'Collections of beach and ocean photographs are cited as visual support.', NULL, '{}', 0, '{"type": "observational", "score": 50.0, "rationale": "Visual examples are easy to gather but hard to interpret precisely."}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:43.314014-04');
INSERT INTO public.nodes VALUES ('P1', 1, 'claim', 'Beach and ocean horizons appear straight', 'At sea level, the horizon usually appears flat and level.', 'observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:03:59.477542-04');
INSERT INTO public.nodes VALUES ('O1', 1, 'objection', 'Human perception is a poor curvature detector', 'At normal scales, human vision is not a reliable way to detect Earth''s curvature.', 'visual-limit', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:04:16.680417-04');
INSERT INTO public.nodes VALUES ('E2', 1, 'evidence', 'Canal observations', 'Flat-earth arguments often cite calm water surfaces and canal observations.', NULL, '{}', 0, '{"type": "observational", "score": 50.0}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:04:47.567307-04');
INSERT INTO public.nodes VALUES ('P2', 1, 'claim', 'Canals and lakes look level', 'Large bodies of water appear level over long distances.', 'observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:05:05.810595-04');
INSERT INTO public.nodes VALUES ('O3', 1, 'objection', 'Water level locally does not imply global flatness', 'A surface can be locally level while still conforming to a sphere.', 'geometry', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:06:39.992308-04');
INSERT INTO public.nodes VALUES ('E3', 1, 'evidence', 'Passenger flight photos', 'Passenger photos and videos from aircraft are used as support.', NULL, '{}', 0, '{"type": "video", "score": 50.0}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:07:14.506006-04');
INSERT INTO public.nodes VALUES ('O4', 1, 'objection', 'Camera lenses can distort curvature', 'Lens choice, framing, and post-processing can affect whether curvature appears visible.', 'imaging', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:07:37.892403-04');
INSERT INTO public.nodes VALUES ('P4', 1, 'claim', 'Balloon footage can look flat', 'Some high-altitude footage appears to show a flat horizon.', 'visual-observation', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:08:07.898273-04');
INSERT INTO public.nodes VALUES ('O5', 1, 'objection', 'Balloon footage is not a controlled measurement', 'Anecdotal video clips are weaker than calibrated measurement.', 'methodology', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:08:45.158589-04');
INSERT INTO public.nodes VALUES ('O9', 1, 'objection', 'Multiple support branches rely on the same observational weakness', 'Several flat-earth arguments share the same limitations of casual observation.', 'cross-cutting', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:13.143537-04');
INSERT INTO public.nodes VALUES ('E5', 1, 'evidence', 'Long-distance photo examples', 'Photos of distant buildings and mountains are used to argue visibility is too great.', NULL, '{}', 0, '{"type": "observational", "score": 50.0}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:09:45.311653-04');
INSERT INTO public.nodes VALUES ('E6', 1, 'evidence', 'Amateur measurement experiments', 'Laser tests, zoom tests, and line-of-sight measurements are cited in support.', NULL, '{}', 0, '{"type": "experimental", "score": 50.0}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:04.425045-04');
INSERT INTO public.nodes VALUES ('P5', 1, 'claim', 'Distant skylines remain visible', 'Some city skylines or mountains appear visible from surprising distances.', 'distance-claims', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:18.391606-04');
INSERT INTO public.nodes VALUES ('P6', 1, 'claim', 'Laser and line-of-sight tests look level', 'Some amateur tests report level measurements over water.', 'measurement', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:10:53.474823-04');
INSERT INTO public.nodes VALUES ('O8', 1, 'objection', 'Institutional agreement is not by itself evidence of conspiracy', 'Broad agreement can reflect converging evidence rather than coordinated deception.', 'meta-claim', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:11:25.937605-04');
INSERT INTO public.nodes VALUES ('O2', 1, 'objection', 'Atmospheric refraction affects visibility', 'Refraction can make distant objects appear higher or more visible than expected.', 'optics', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:11:48.190903-04');
INSERT INTO public.nodes VALUES ('O7', 1, 'objection', 'Amateur tests often lack controls', 'DIY experiments frequently omit calibration, repeatability, and environmental controls.', 'methodology', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:02.593103-04');
INSERT INTO public.nodes VALUES ('O6', 1, 'objection', 'Distance claims depend on elevation and hidden-height math', 'Visibility calculations depend on observer height, object height, and atmospheric conditions.', 'geometry', '{}', 0, '{}', '2026-04-13 11:42:53.694065-04', '2026-07-05 12:12:30.934642-04');


--
-- TOC entry 3645 (class 0 OID 0)
-- Dependencies: 219
-- Name: graphs_id_seq; Type: SEQUENCE SET; Schema: public; Owner: postgres
--

SELECT pg_catalog.setval('public.graphs_id_seq', 1, true);


--
-- TOC entry 3472 (class 2606 OID 18081)
-- Name: edges edges_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_pkey PRIMARY KEY (id);


--
-- TOC entry 3478 (class 2606 OID 18083)
-- Name: graphs graphs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.graphs
    ADD CONSTRAINT graphs_pkey PRIMARY KEY (id);


--
-- TOC entry 3480 (class 2606 OID 18085)
-- Name: graphs graphs_slug_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.graphs
    ADD CONSTRAINT graphs_slug_key UNIQUE (slug);


--
-- TOC entry 3486 (class 2606 OID 18087)
-- Name: nodes nodes_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.nodes
    ADD CONSTRAINT nodes_pkey PRIMARY KEY (id);


--
-- TOC entry 3473 (class 1259 OID 18088)
-- Name: ix_edges_from_node_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_from_node_id ON public.edges USING btree (from_node_id);


--
-- TOC entry 3474 (class 1259 OID 18089)
-- Name: ix_edges_graph_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_graph_id ON public.edges USING btree (graph_id);


--
-- TOC entry 3475 (class 1259 OID 18090)
-- Name: ix_edges_kind; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_kind ON public.edges USING btree (kind);


--
-- TOC entry 3476 (class 1259 OID 18091)
-- Name: ix_edges_to_node_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_edges_to_node_id ON public.edges USING btree (to_node_id);


--
-- TOC entry 3481 (class 1259 OID 18092)
-- Name: ix_nodes_category; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_category ON public.nodes USING btree (category);


--
-- TOC entry 3482 (class 1259 OID 18093)
-- Name: ix_nodes_evidence_gin; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_evidence_gin ON public.nodes USING gin (evidence);


--
-- TOC entry 3483 (class 1259 OID 18094)
-- Name: ix_nodes_graph_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_graph_id ON public.nodes USING btree (graph_id);


--
-- TOC entry 3484 (class 1259 OID 18095)
-- Name: ix_nodes_kind; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX ix_nodes_kind ON public.nodes USING btree (kind);


--
-- TOC entry 3487 (class 2606 OID 18096)
-- Name: edges edges_from_node_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_from_node_id_fkey FOREIGN KEY (from_node_id) REFERENCES public.nodes(id) ON DELETE CASCADE;


--
-- TOC entry 3488 (class 2606 OID 18101)
-- Name: edges edges_graph_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_graph_id_fkey FOREIGN KEY (graph_id) REFERENCES public.graphs(id) ON DELETE CASCADE;


--
-- TOC entry 3489 (class 2606 OID 18106)
-- Name: edges edges_to_node_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.edges
    ADD CONSTRAINT edges_to_node_id_fkey FOREIGN KEY (to_node_id) REFERENCES public.nodes(id) ON DELETE CASCADE;


--
-- TOC entry 3490 (class 2606 OID 18111)
-- Name: nodes nodes_graph_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.nodes
    ADD CONSTRAINT nodes_graph_id_fkey FOREIGN KEY (graph_id) REFERENCES public.graphs(id) ON DELETE CASCADE;


-- Completed on 2026-07-05 12:23:04 EDT

--
-- PostgreSQL database dump complete
--

