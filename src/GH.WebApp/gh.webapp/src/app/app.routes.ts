import { RouterModule, Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { UserSessionComponent } from './user-session/user-session.component';
import { NgModule } from '@angular/core';

export const routes: Routes = [
    { path: '', component: HomeComponent, pathMatch: 'full'},
    { path: 'user-session', component: UserSessionComponent},
];

@NgModule({
    imports: [RouterModule.forRoot(routes)],
    exports: [RouterModule]
})
export class AppRoutingModule { }


