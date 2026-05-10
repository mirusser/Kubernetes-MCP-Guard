### Summary

This video tutorial provides a comprehensive guide on how to configure **Keycloak** to use **Postgres** as its production-ready database, replacing the default embedded database that is suitable only for development or testing environments. The presenter demonstrates setting up a **Postgres 17** instance via Docker Compose, preparing the database schema, and configuring Keycloak to connect to this external database. The tutorial also covers validating the connection, inspecting the resulting database schema and tables, and finally, integrating Keycloak authentication with a .NET API, including user registration and token-based authorization.

---

### Key Steps and Configuration

- **Postgres Setup via Docker Compose:**
  - Service name: `Postgres`
  - Docker image: `postgres:17`
  - Environment variables:
    - `POSTGRES_USER=postgres`
    - `POSTGRES_PASSWORD=postgres` *(dummy password for testing)*
    - `POSTGRES_DB=keycloakoft` *(custom database name)*
  - Volume mapping to persist data locally
  - Exposed port: `5432`

- **Database Schema Initialization:**
  - Connect to Postgres and execute SQL:
    $$
    \text{CREATE SCHEMA IF NOT EXISTS Keycloak;}
    $$
  - Purpose: Isolate Keycloak tables from other application tables.

- **Keycloak Service Configuration:**
  - Add dependency on Postgres service in Docker Compose.
  - Environment variables for Keycloak:
  
| Variable          | Description                                      | Value Example          |
|-------------------|-------------------------------------------------|-----------------------|
| `KC_DB`           | Database type Keycloak connects to               | `postgres`            |
| `KC_DB_USERNAME`  | Database username                                 | `postgres`            |
| `KC_DB_PASSWORD`  | Database password                                 | `postgres`            |
| `KC_DB_SCHEMA`    | Database schema used for Keycloak tables         | `keycloak`            |
| `KC_DB_URL_HOST`  | Hostname of Postgres instance (matches service) | `postgres`            |
| `KC_DB_PORT`      | Port number for Postgres                          | `5432`                |
| `KC_DB_URL_DATABASE` | Database name inside Postgres                     | `keycloakoft`         |

- Keycloak is configured to **restart until it successfully connects** to the Postgres instance.

---

### Validation and Usage

- After deployment, Keycloak auto-generates **90 tables** within the `keycloak` schema.
- The schema includes critical tables such as:
  - `user_entity`: stores user data, including test users and their realm associations.
  - `realm`: stores realm information, including the default master realm and custom realms.
  - `credential`: stores user credential data, including password hashes and metadata.

- Keycloak uses the **Argon2** hashing algorithm by default for passwords, storing:
  - The hash value (including salt).
  - Number of iterations.
  - Hashing algorithm type.
  - Other configuration parameters.

- The **realm** serves as the core entity linking users, clients, and other authentication data.
- Keycloak data model is complex, reflecting the comprehensive authentication and authorization functionality.

---

### User Authentication Flow Demonstrated

- User can **register** through the Keycloak UI.
- Client configuration includes setting redirect URIs and web origins.
- API clients authenticate through Keycloak using OAuth2/OpenID Connect flows.
- Access tokens are issued and used to authorize API requests.
- The presenter tests this integration using Swagger UI to authenticate and make authorized requests.

---

### Important Takeaways

- **Using Postgres as the backing database is the recommended approach for production deployments of Keycloak**, rather than the default embedded database.
- Setting up Postgres requires initializing a dedicated schema to isolate Keycloak data.
- Keycloak’s environment variables control connection details and database schema usage.
- The internal Keycloak database schema is extensive and crucial for managing realms, users, credentials, and clients.
- Security best practices such as **two-factor authentication (2FA)** are important and can be implemented in the application layer (demonstrated for ASP.NET Core using TOTP).

---

### Additional Notes

- The source code for this setup is available for free (provided in the video’s pinned comment).
- The tutorial emphasizes the importance of environment variable configuration and Docker Compose orchestration for managing dependencies and services.
- The video does *not specify* production-ready password policies or SSL configurations for Postgres connections—these would need to be addressed separately in a real deployment.

---

### Summary Table: Key Environment Variables for Keycloak-Postgres Integration

| Variable            | Purpose                                      | Example Value      |
|---------------------|----------------------------------------------|--------------------|
| $KC\_DB$            | Type of database Keycloak connects to        | `postgres`         |
| $KC\_DB\_USERNAME$  | Database user for connection                   | `postgres`         |
| $KC\_DB\_PASSWORD$  | Database user password                          | `postgres`         |
| $KC\_DB\_SCHEMA$    | Schema in the database for Keycloak data      | `keycloak`         |
| $KC\_DB\_URL\_HOST$ | Hostname of Postgres service                    | `postgres`         |
| $KC\_DB\_PORT$      | Database port number                            | `5432`             |
| $KC\_DB\_URL\_DATABASE$ | Database name inside Postgres                   | `keycloakoft`      |

---

This video serves as a practical, step-by-step guide for deploying Keycloak with Postgres in a Dockerized environment, emphasizing best practices for production readiness and integration with application APIs.