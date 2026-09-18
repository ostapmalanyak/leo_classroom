import { bootstrapApplication } from '@angular/platform-browser';
import { createAppConfig } from './app/app.config';
import { loadRuntimeConfig } from './core/auth/auth-config';
import { App } from './app/app';

// the deployment's URLs have to be known before Keycloak is configured, so they are fetched first
loadRuntimeConfig()
  .then(() => bootstrapApplication(App, createAppConfig()))
  .catch((err) => console.error(err));
