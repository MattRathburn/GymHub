import { Component } from 'react';
import { NavMenu } from '../NavMenu/NavMenu';
import { Outlet } from 'react-router-dom';

export class Layout extends Component {
  static displayName = Layout.name;

  render () {
    return (
      <div>
        <NavMenu />
        <Outlet />
      </div>
    );
  }
}