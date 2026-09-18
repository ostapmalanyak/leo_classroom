import { HttpClient, HttpParams } from '@angular/common/http';
import { Directive, inject } from '@angular/core';
import { backendApiBaseUrl } from '../auth/auth-config';

@Directive()
export abstract class BackendServiceBase {
  protected readonly http: HttpClient = inject(HttpClient);

  protected abstract get controller(): string;

  protected buildUrl(action: string | null): string {
    let url = `${backendApiBaseUrl()}/${this.controller}`;
    if (action !== null) {
      url = `${url}/${action}`;
    }

    return url;
  }

  protected createHttpParams(...params: (QueryParam | null)[]): HttpParams {
    let httpParams = new HttpParams();
    for (const param of params.filter(p => p != null && p[1] != null)) {
      const [key, value] = param as QueryParam;
      httpParams = httpParams.append(key, String(value));
    }

    return httpParams;
  }
}

export type QueryParam = [string, unknown];
