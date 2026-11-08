import { Route } from '@angular/router';
import { authGuard, trainerGuard } from './core/auth/auth.guards';

export const appRoutes: Route[] = [
  {
    path: '',
    title: 'GymHub · Programs',
    loadComponent: () => import('./features/programs/program-list.component').then((m) => m.ProgramListComponent),
  },
  {
    path: 'programs/:id',
    title: 'GymHub · Program',
    loadComponent: () => import('./features/programs/program-detail.component').then((m) => m.ProgramDetailComponent),
  },
  {
    path: 'me/programs',
    title: 'GymHub · My programs',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/subscriptions/my-subscriptions.component').then((m) => m.MySubscriptionsComponent),
  },
  {
    path: 'studio',
    title: 'GymHub · Trainer studio',
    canActivate: [trainerGuard],
    loadComponent: () => import('./features/programs/trainer/my-programs.component').then((m) => m.MyProgramsComponent),
  },
  {
    path: 'studio/programs/:id',
    title: 'GymHub · Edit program',
    canActivate: [trainerGuard],
    loadComponent: () =>
      import('./features/programs/trainer/program-editor.component').then((m) => m.ProgramEditorComponent),
  },
  { path: '**', redirectTo: '' },
];
