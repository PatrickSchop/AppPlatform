import { JSX } from 'react';
import type { BackgroundTask } from '@PS/app-client';
import { useBackgroundTasks } from './hooks';

export function TaskProgress(): JSX.Element {
  const { tasks } = useBackgroundTasks();

  return (
    <div className="ps-task-progress">
      {tasks.map((task: BackgroundTask) => (
        <div key={task.id} className="ps-task-item">
          <div className="ps-task-header">
            <span className="ps-task-type">{task.taskType}</span>
            <span className="ps-task-status">{task.status}</span>
          </div>
          {task.description && (
            <div className="ps-task-description">{task.description}</div>
          )}
          <div className="ps-task-progress-bar">
            <div
              className="ps-task-progress-fill"
              style={{ width: `${task.completionPercentage}%` }}
            />
          </div>
          <div className="ps-task-percentage">{task.completionPercentage}%</div>
        </div>
      ))}
    </div>
  );
}
