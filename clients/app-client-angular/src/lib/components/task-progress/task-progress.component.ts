import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BackgroundTaskService } from '../../background-task.service';

@Component({
  selector: 'PS-task-progress',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="ps-task-progress">
      <div *ngFor="let task of tasks()" class="ps-task-item">
        <div class="ps-task-header">
          <span class="ps-task-type">{{ task.taskType }}</span>
          <span class="ps-task-status">{{ task.status }}</span>
        </div>
        <div *ngIf="task.description" class="ps-task-description">
          {{ task.description }}
        </div>
        <div class="ps-task-progress-bar">
          <div
            class="ps-task-progress-fill"
            [style.width.%]="task.completionPercentage"
          ></div>
        </div>
        <div class="ps-task-percentage">{{ task.completionPercentage }}%</div>
      </div>
    </div>
  `,
})
export class TaskProgressComponent {
  private taskService = inject(BackgroundTaskService);
  tasks = this.taskService.tasks;
}
