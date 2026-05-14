# Reasoning Graph Insights Engine

A graph analysis platform that explores how reasoning networks can be evaluated for structural weaknesses, resilience, and insight generation using exact and approximation algorithms.

This project is part of **LogicLikely**, where structured reasoning systems are being explored as a way to improve online discourse.

---

## Project Structure

```text
/backend   -> C# Web API + PostgreSQL integration
/frontend  -> React application for graph visualization
/docs      -> Project documentation
```

---

## Getting Started

Clone the repository:

```bash
git clone git@github.com:LogicLikely/reasoning-graph-insights-engine.git
cd reasoning-graph-insights-engine
```

---

# Backend

Run the API from the project root:

```bash
dotnet run --project backend/backend.csproj
```

Run backend tests:

```bash
dotnet test
```

---

## Database Configuration

Set your PostgreSQL connection string as an environment variable:

```bash
export Database__ConnectionString="Host=localhost;Database=insights;Username=postgres;Password=YourPasswordHere"
```

---

## CORS Configuration

The backend currently allows all origins for development purposes.

To restrict frontend access, update `Program.cs` and set an allowed origin:

Local development:

```bash
export Cors__AllowedOrigins__0=http://localhost:5173
```

Production example:

```bash
export Cors__AllowedOrigins__0=https://logiclikely.com
```

---

# Frontend

From the `/frontend` directory:

```bash
npm install
npm run dev
```

Run frontend tests:

```bash
npm run test
```
