# 25. Dynamic Csproj Discovery for Docker Builds

Date: 2026-05-30

## Status

Accepted

## Context

As the number of .NET projects (both executables and shared class libraries) grows, our Dockerfiles were becoming large and difficult to maintain. Each time a new project dependency was added to a runnable host like Planner, Observer, Executor, or the MCP Gateway, we had to manually append `COPY` statements for its `.csproj` file in the Docker build stages to take advantage of Docker layer caching for `dotnet restore`. 

This led to "Dockerfile fatigue" and frequent build breaks when dependencies were updated in code but forgotten in the Dockerfiles.

## Decision

We have adopted the **"Scratch Stage" Extraction Pattern** for our Docker builds. 

Our Dockerfiles now use an initial Alpine `filter` stage to copy the entire repository and dynamically find and isolate just the `.csproj` files into a clean directory structure using `find` and `tar`.

```dockerfile
FROM alpine:3.21 AS filter
WORKDIR /src
COPY . .
RUN mkdir /out && \
    find . -name '*.csproj' | tar -cf - -T - | tar -xf - -C /out
```

The subsequent build stage then copies these isolated project files for the `dotnet restore` step, and finally copies the remaining `src/` content for the actual build:

```dockerfile
COPY --from=filter /out/src/ ./src/
RUN dotnet restore ...
COPY src/ src/
RUN dotnet publish ...
```

## Consequences

*   **Pros:** 
    *   No more manual maintenance of `COPY` commands for individual `.csproj` files.
    *   Adding a new project dependency inside the solution will naturally restore correctly without Dockerfile changes.
    *   We maintain the exact same Docker caching efficiency (the `restore` layer is cached as long as no `.csproj` file changes).
*   **Cons:** 
    *   The `COPY src/ src/` step copies the entire source folder tree (including test projects), which slightly increases the temporary image size during the intermediate build stage. However, since we use multi-stage builds and only copy the published `/app` folder to the final runtime image, this has zero impact on the final container size.
    *   A slightly more complex initial stage using `tar` pipes that may look unfamiliar to some engineers.
