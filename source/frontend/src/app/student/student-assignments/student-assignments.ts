import { Component, computed, inject, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatProgressBar } from '@angular/material/progress-bar';
import { Instant } from '@js-joda/core';
import { AssignedAssignment, AssignmentService } from '../../../core/services/assignment-service';
import { SnackbarService } from '../../../core/services/snackbar-service';

interface CourseTile {
  id: number;
  title: string;
  initials: string;
  color: string;
  open: number;
}

@Component({
  selector: 'app-student-assignments',
  imports: [RouterLink, MatProgressBar],
  templateUrl: './student-assignments.html',
  styleUrl: './student-assignments.scss'
})
export class StudentAssignments implements OnInit {
  private readonly service = inject(AssignmentService);
  private readonly snackbar = inject(SnackbarService);

  private readonly all: WritableSignal<AssignedAssignment[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly courses: Signal<CourseTile[]> = computed(() => {
    const now = Instant.now();
    const byCourse = new Map<number, CourseTile>();
    for (const a of this.all()) {
      const tile = byCourse.get(a.courseId) ?? {
        id: a.courseId,
        title: a.courseTitle,
        initials: a.courseTitle.trim().slice(0, 2).toUpperCase(),
        // stable per course, so a course keeps its colour between visits
        color: `hsl(${(a.courseId * 137) % 360} 45% 38%)`,
        open: 0
      };
      if (a.deadline === null || !a.deadline.isBefore(now)) {
        tile.open++;
      }
      byCourse.set(a.courseId, tile);
    }

    return [...byCourse.values()].sort((l, r) => l.title.localeCompare(r.title));
  });

  public async ngOnInit(): Promise<void> {
    this.loading.set(true);
    const result = await this.service.listMine();
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load your assignments');

      return;
    }
    this.all.set([...result.accepted, ...result.unaccepted]);
  }
}
