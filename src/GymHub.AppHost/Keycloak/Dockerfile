# Production Keycloak: the GymHub realm baked into the import folder (Container Apps has no bind mounts) and
# Postgres as its database. Secrets and addresses come from environment variables at run time; the realm file
# reads them with ${...} placeholders on import. Keep the tag in step with Aspire's Keycloak image.
FROM quay.io/keycloak/keycloak:26.5

ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true

COPY realm/ /opt/keycloak/data/import/

RUN /opt/keycloak/bin/kc.sh build
