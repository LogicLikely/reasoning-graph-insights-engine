# reasoning-graph-insights-engine

Graph-based analysis engine for identifying structural vulnerabilities in reasoning networks using exact and approximation algorithms.

# backend commands (from top-level folder)

dotnet run --project backend/backend.csproj
dotnet test

## notes

DB password is stored like so...

export Database\_\_ConnectionString="Host=localhost;Database=insights;Username=postgres;Password=PasswordGoesHere"

## future security

We need to allow the frontend origin to get through CORS to connect to the backend for browsers. Right now
we are letting everything through. But in the backend Program.cs, we can make one edit to lock things
down. Then set an environment variable like either...

export Cors\_\_AllowedOrigins\_\_0=http://localhost:5173
...or...
export Cors\_\_AllowedOrigins\_\_0=http://logiclikely.com

# frontend commands (from frontend folder)

npm run dev
npm run test
