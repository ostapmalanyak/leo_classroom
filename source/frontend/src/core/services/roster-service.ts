import { Injectable } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { z } from 'zod';
import { BackendServiceBase } from './backend-service-base';
import { UserSummary, userSummaryZod } from './user-service';

export type RosterWriteResult<T> = T | 'conflict' | 'forbidden' | 'not-found' | null;
export type MemberWriteResult = 'ok' | 'forbidden' | 'not-found' | null;
export type DeleteRosterResult = 'ok' | { courseCount: number } | 'forbidden' | 'not-found' | null;

@Injectable({
  providedIn: 'root'
})
export class RosterService extends BackendServiceBase {
  protected override get controller(): string {
    return 'rosters';
  }

  public async getRosters(): Promise<RosterOverview[] | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl('')));

      return rosterOverviewZod.array().parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async getMembers(id: number): Promise<UserSummary[] | null> {
    try {
      const response = await firstValueFrom(this.http.get<unknown>(this.buildUrl(`${id}/members`)));

      return userSummaryZod.array().parse(response);
    } catch (error) {
      return this.mapReadError(error);
    }
  }

  public async createRoster(name: string): Promise<RosterWriteResult<Roster>> {
    try {
      const response = await firstValueFrom(this.http.post<unknown>(this.buildUrl(''), { name }));

      return rosterZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async renameRoster(id: number, name: string): Promise<RosterWriteResult<Roster>> {
    try {
      const response = await firstValueFrom(this.http.put<unknown>(this.buildUrl(`${id}`), { name }));

      return rosterZod.parse(response);
    } catch (error) {
      return this.mapWriteError(error);
    }
  }

  public async deleteRoster(id: number): Promise<DeleteRosterResult> {
    try {
      await firstValueFrom(this.http.delete(this.buildUrl(`${id}`)));

      return 'ok';
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 409) {
          const count = z.number().int().nonnegative().catch(0).parse(error.error?.courseCount);

          return { courseCount: count };
        }
        if (error.status === 403) {
          return 'forbidden';
        }
        if (error.status === 404) {
          return 'not-found';
        }

        return null;
      }
      throw error;
    }
  }

  public async addMember(rosterId: number, userId: number): Promise<MemberWriteResult> {
    return this.mutateMember('post', rosterId, userId);
  }

  public async removeMember(rosterId: number, userId: number): Promise<MemberWriteResult> {
    return this.mutateMember('delete', rosterId, userId);
  }

  private async mutateMember(method: 'post' | 'delete', rosterId: number,
                             userId: number): Promise<MemberWriteResult> {
    const url = this.buildUrl(`${rosterId}/members/${userId}`);
    try {
      await firstValueFrom(method === 'post' ? this.http.post(url, null) : this.http.delete(url));

      return 'ok';
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 403) {
          return 'forbidden';
        }
        if (error.status === 404) {
          return 'not-found';
        }

        return null;
      }
      throw error;
    }
  }

  private mapReadError(error: unknown): null {
    if (error instanceof HttpErrorResponse) {
      return null;
    }
    throw error;
  }

  private mapWriteError(error: unknown): 'conflict' | 'forbidden' | 'not-found' | null {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 409) {
        return 'conflict';
      }
      if (error.status === 403) {
        return 'forbidden';
      }
      if (error.status === 404) {
        return 'not-found';
      }

      return null;
    }
    throw error;
  }
}

export enum RosterKind {
  Auto = 'Auto',
  Custom = 'Custom'
}

const rosterOverviewZod = z.object({
  id: z.int().positive(),
  name: z.string().min(1),
  kind: z.enum(RosterKind),
  memberCount: z.int().nonnegative(),
  ownerId: z.int().positive().nullable()
});
export type RosterOverview = z.infer<typeof rosterOverviewZod>;

const rosterZod = z.object({
  id: z.int().positive(),
  name: z.string().min(1),
  kind: z.enum(RosterKind),
  ownerId: z.int().positive().nullable()
});
export type Roster = z.infer<typeof rosterZod>;
