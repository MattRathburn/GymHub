import { Route } from '@angular/router';
import { adminGuard, authGuard, trainerGuard } from './core/auth/auth.guards';

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
    path: 'programs/:id/discussion',
    title: 'GymHub · Discussion',
    canActivate: [authGuard],
    loadComponent: () => import('./features/discussions/board.component').then((m) => m.BoardComponent),
  },
  {
    path: 'programs/:id/discussion/:threadId',
    title: 'GymHub · Discussion',
    canActivate: [authGuard],
    loadComponent: () => import('./features/discussions/thread.component').then((m) => m.ThreadComponent),
  },
  {
    path: 'me/profile',
    title: 'GymHub · Your profile',
    canActivate: [authGuard],
    loadComponent: () => import('./features/trainers/member-profile.component').then((m) => m.MemberProfileComponent),
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
  {
    path: 'studio/profile',
    title: 'GymHub · Trainer profile',
    canActivate: [trainerGuard],
    loadComponent: () => import('./features/trainers/studio-profile.component').then((m) => m.StudioProfileComponent),
  },
  {
    path: 'trainers/apply',
    title: 'GymHub · Become a trainer',
    canActivate: [authGuard],
    loadComponent: () => import('./features/trainers/apply.component').then((m) => m.ApplyComponent),
  },
  {
    path: 'trainers/:id',
    title: 'GymHub · Trainer',
    loadComponent: () => import('./features/trainers/trainer-profile.component').then((m) => m.TrainerProfileComponent),
  },
  {
    path: 'me/following',
    title: 'GymHub · Following',
    canActivate: [authGuard],
    loadComponent: () => import('./features/trainers/following.component').then((m) => m.FollowingComponent),
  },
  {
    path: 'admin/trainer-applications',
    title: 'GymHub · Trainer applications',
    canActivate: [adminGuard],
    loadComponent: () =>
      import('./features/trainers/admin/trainer-applications.component').then((m) => m.TrainerApplicationsComponent),
  },
  {
    path: 'admin/reports',
    title: 'GymHub · Reported posts',
    canActivate: [adminGuard],
    loadComponent: () => import('./features/discussions/admin/reports.component').then((m) => m.ReportsComponent),
  },
  { path: '**', redirectTo: '' },
];
