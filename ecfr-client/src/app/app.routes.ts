import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/agencies',
    pathMatch: 'full'
  },
  {
    path: 'agencies',
    loadComponent: () => import('./components/agency-list/agency-list.component').then(m => m.AgencyListComponent)
  },
  {
    path: 'agencies/:slug',
    loadComponent: () => import('./components/agency-detail/agency-detail.component').then(m => m.AgencyDetailComponent)
  }
];
